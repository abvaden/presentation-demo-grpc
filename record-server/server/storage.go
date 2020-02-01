package main

import (
	"fmt"

	"github.com/go-redis/redis/v7"
	"github.com/golang/protobuf/proto"
	"github.com/google/uuid"
	log "github.com/sirupsen/logrus"
	"v2tools.com/presentations/demos/grpc/record-server/voting"
)

// Storage ..
type Storage interface {
	RecordVote(userID, pollID, optionID string) (bool, int64, error)
	GetPolls() ([]*voting.Poll, error)
	AddPoll(*voting.Poll) (bool, error)
}

// NewStorage ..
func NewStorage(pubSub PubSub) (Storage, error) {
	client, err := GetRedisClient()
	if err != nil {
		return nil, err
	}

	store := redisStorage{
		client: client,
		pubSub: pubSub,
	}
	return &store, nil
}

type redisStorage struct {
	client *redis.Client
	pubSub PubSub
}

const (
	votingRecordKey = "demo-grpc-voting-record"
	pollsKey        = "demo-grpc-voting-polls"
	pollsIncKey     = "demo-grpc-voting-polls-inc"
)

func (store *redisStorage) AddPoll(poll *voting.Poll) (bool, error) {
	if poll == nil || poll.Id == "" {
		return false, nil
	}

	storeValue := proto.MarshalTextString(poll)
	result, err := store.client.HSetNX(pollsKey, poll.Id, storeValue).Result()
	if err != nil {
		log.WithError(err).Warn("Error while storing poll")
	}

	return result, err
}

func (store *redisStorage) RecordVote(userID, pollID, optionID string) (bool, int64, error) {
	if userID == "" || pollID == "" {
		return false, 0, nil
	}

	// Ensure the poll exists first
	result, err := store.client.HGet(pollsKey, pollID).Result()
	if err != nil {
		log.WithError(err).Error("Error while getting voting polls")
		return false, 0, err
	}

	if result == "" {
		return false, 0, nil
	}

	key := fmt.Sprintf("%s:%s", userID, pollID)

	res, err := store.client.SAdd(votingRecordKey, key).Result()
	didSet := res == 1
	newID := int64(0)
	if didSet {
		newID, err = store.client.Incr(pollsIncKey).Result()
	}

	if err != nil {
		log.WithError(err).Error("Error while recording users vote")
		return false, 0, err
	}

	if didSet && newID != 0 {
		votingRecord := voting.VotingRecord{
			Id:       uuid.Microsoft.String(),
			Sequence: newID,
			Vote: &voting.Vote{
				UserId:   userID,
				PollId:   pollID,
				OptionId: optionID,
				UserCode: "-",
			},
		}

		err = store.pubSub.PublishVote(&votingRecord)
		if err != nil {
			log.WithError(err).Error("Error while publishing voting record")
		}
	}

	return didSet, newID, err
}

func (store *redisStorage) GetPolls() ([]*voting.Poll, error) {
	result, err := store.client.HGetAll(pollsKey).Result()
	if err != nil {
		log.WithError(err).Error("Error while getting polls")
		return nil, err
	}

	items := make([]*voting.Poll, len(result))
	i := 0
	for _, value := range result {
		item := &voting.Poll{}
		err := proto.UnmarshalText(value, item)
		if err != nil {
			log.WithError(err).Error("Error parsing db value as a poll")
			continue
		}

		items[i] = item
		i++
	}

	return items, nil
}
