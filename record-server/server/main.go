package main

import (
	"fmt"
	"math/rand"
	"net"
	"os"
	"time"

	log "github.com/sirupsen/logrus"
	"google.golang.org/grpc"
	"v2tools.com/presentations/demos/grpc/record-server/voting"
)

func main() {
	listener, err := net.Listen("tcp", ":50000")
	if err != nil {
		log.Panicf("Error while starting listener on port 50000 %f", err)
	}

	pubSub, err := NewPubSub()
	if err != nil {
		log.Panicf("Error while initializing pub sub engine %f", err)
	}
	storage, err := NewStorage(pubSub)
	if err != nil {
		log.Panicf("Error while initializing storage engine %f", err)
	}
	for _, poll := range getSamplePolls() {
		storage.AddPoll(poll)
	}

	usersServiceHost := os.Getenv("USERS_SERVICE_HOST")
	usersServicePort := os.Getenv("USERS_SERVICE_PORT")
	if usersServiceHost == "" {
		usersServiceHost = "localhost"
	}
	if usersServicePort == "" {
		usersServicePort = "5000"
	}
	clientConnection, err := grpc.Dial(fmt.Sprintf("%s:%s", usersServiceHost, usersServicePort), grpc.WithInsecure())
	if err != nil {
		log.WithError(err).Panicf("Error while dialing the users service")
	}

	usersClient := voting.NewUsersClient(clientConnection)

	recordServer, err := NewServer(storage, pubSub, usersClient)
	if err != nil {
		log.Panicf("Error while initializing presentation server %f", err)
	}

	serverOpts := grpc.EmptyServerOption{}
	grpcServer := grpc.NewServer(serverOpts)

	grpcServeError := make(chan error)

	go func() {

		voting.RegisterRecordingServiceServer(grpcServer, recordServer)
		if err = grpcServer.Serve(listener); err != nil {
			grpcServeError <- err
		}
	}()

	err = <-grpcServeError
	log.WithError(err).Fatal("Error while listening for incoming connections")
}

func getSamplePolls() []*voting.Poll {
	polls := make([]*voting.Poll, 3)
	polls[0] = &voting.Poll{
		Summary: "Best dad joke",
		Id:      "kawqfuaulhzziczo",
		Order:   0,
		Type:    voting.PollType_Single,
		Options: []*voting.Option{
			&voting.Option{
				Id:   "lfyvrcfkoltuqpib",
				Name: "Why do you have to act quickly in a flood? Because it's an emergent sea!",
			},
			&voting.Option{
				Id:   "tngtsornxbdxrvzu",
				Name: "I just watched a video of a drill. It was a bit boring",
			},
			&voting.Option{
				Id:   "yrquewzumfiieehe",
				Name: "I got a reversible jacket for Christmas, I can't wait to see how it turns out.",
			},
		},
	}

	polls[1] = &voting.Poll{
		Summary: "How do you ship code",
		Id:      "pqqaumdlvtryvask",
		Order:   1,
		Type:    voting.PollType_Single,
		Options: []*voting.Option{
			&voting.Option{
				Id:   "ncdghcvrvlwoawkk",
				Name: "Unit test everything!",
			},
			&voting.Option{
				Id:   "vkeykudmipgebmsx",
				Name: "Testing what is that?",
			},
			&voting.Option{
				Id:   "uikomknveivrjtwv",
				Name: "Production is my test bed!",
			},
		},
	}

	polls[2] = &voting.Poll{
		Summary: "Which programming languages do you use",
		Id:      "rjzmrwqemmntowuf",
		Order:   1,
		Type:    voting.PollType_Single,
		Options: []*voting.Option{
			&voting.Option{
				Id:   "ocsvysqsrbxjteiv",
				Name: "C#",
			},
			&voting.Option{
				Id:   "ocsvysqsrbxjteiv",
				Name: "Java",
			},
			&voting.Option{
				Id:   "ocsvysqsrbxjteiv",
				Name: "Python",
			},
			&voting.Option{
				Id:   "qlebononsczftyjh",
				Name: "JavaScript",
			},
		},
	}
	return polls
}

var randGenerator *rand.Rand = rand.New(
	rand.NewSource(time.Now().UnixNano()))

const charset = "abcdefghijklmnopqrstuvwxyz"

//RandomID ..
func RandomID() string {
	data := make([]byte, 16)
	for i := range data {
		data[i] = charset[randGenerator.Intn(len(charset))]
	}
	return string(data)
}
