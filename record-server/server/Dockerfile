FROM golang:1.13.6-alpine3.10 as build

COPY record-server/server/go.sum /app/record-server/server/go.sum
COPY record-server/server/go.mod /app/record-server/server/go.mod

WORKDIR /app/record-server/server/
RUN go mod download

COPY record-server/server/ /app/record-server/server/
RUN go build -o server

FROM alpine
COPY --from=build /app/record-server/server/server /app/server
ENTRYPOINT /app/server