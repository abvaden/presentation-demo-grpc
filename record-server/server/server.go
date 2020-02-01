package main

import (
	"context"

	"github.com/golang/protobuf/ptypes/empty"
	log "github.com/sirupsen/logrus"
	"v2tools.com/presentations/demos/grpc/record-server/voting"
)

const invalidUserMessage = "Invlaid user"
const internalErrorMesage = "Internal error"
const voteAlreadyRecordedMessage = "Vote already recorded"

// RecordServer ..
type RecordServer struct {
	store  Storage
	pubSub PubSub
	users  voting.UsersClient
}

//NewServer ..
func NewServer(store Storage, pubSub PubSub, users voting.UsersClient) (*RecordServer, error) {
	return &RecordServer{
		store:  store,
		pubSub: pubSub,
		users:  users,
	}, nil
}

//GetPolls ..
func (server *RecordServer) GetPolls(ctx context.Context, req *empty.Empty) (*voting.GetPollsResponse, error) {
	polls, err := server.store.GetPolls()
	if err != nil {
		return &voting.GetPollsResponse{
			Error: true,
		}, nil
	}

	return &voting.GetPollsResponse{
		Error: false,
		Polls: polls,
	}, nil
}

//AddPoll ..
func (server *RecordServer) AddPoll(ctx context.Context, req *voting.Poll) (*voting.AddPollResponse, error) {
	req.Id = RandomID()
	for _, option := range req.Options {
		option.Id = RandomID()
	}
	didAdd, err := server.store.AddPoll(req)
	if err != nil || !didAdd {
		return &voting.AddPollResponse{
			Error: true,
		}, nil
	}

	log.Infof("Added poll %s with Id %s", req.Summary, req.Id)
	return &voting.AddPollResponse{
		Error: false,
		Poll:  req,
	}, nil
}

// RecordVote ..
func (server *RecordServer) RecordVote(ctx context.Context, req *voting.Vote) (*voting.RecordVoteResponse, error) {
	user := voting.User{
		Name: req.UserId,
		Code: req.UserCode,
	}

	if user.Name == "" || user.Code == "" {
		return &voting.RecordVoteResponse{
			Error:        true,
			ErrorMessage: invalidUserMessage,
		}, nil
	}

	userResponse, err := server.users.ValidateUser(ctx, &user)
	if err != nil {
		log.WithError(err).Warn("Error while validating user with user service")
		return &voting.RecordVoteResponse{
			Error:        true,
			ErrorMessage: internalErrorMesage,
		}, nil
	}

	if userResponse.Error {
		return &voting.RecordVoteResponse{
			Error:        true,
			ErrorMessage: invalidUserMessage,
		}, nil
	}

	ok, _, err := server.store.RecordVote(req.UserId, req.PollId, req.OptionId)
	if err != nil || !ok {
		if err != nil {
			log.WithError(err).Warn("Error recording vote")
			return &voting.RecordVoteResponse{
				Error:        true,
				ErrorMessage: internalErrorMesage,
			}, nil
		}
		return &voting.RecordVoteResponse{
			Error:        true,
			ErrorMessage: voteAlreadyRecordedMessage,
		}, nil
	}

	return &voting.RecordVoteResponse{
		Error: false,
	}, nil
}

// StreamRecords ..
func (server *RecordServer) StreamRecords(req *voting.StreamRecordsRequest, srv voting.RecordingService_StreamRecordsServer) error {
	done := make(chan bool)
	defer close(done)
	out, err := server.pubSub.SubscribeVotes(done)
	if err != nil {
		log.WithError(err).Error("Error while subscribing to votes")
		return nil
	}
	for {
		select {
		case votingRecord, more := <-out:
			if !more {
				return nil
			}

			err := srv.Send(&votingRecord)
			if err != nil {
				log.WithError(err).Info("Error while sending voting record to client")
				return nil
			}
		case <-srv.Context().Done():
			return nil
		}
	}
}
