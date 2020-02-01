FROM mcr.microsoft.com/dotnet/core/sdk:3.1 as build

WORKDIR /app/

COPY /protos /app/protos/
COPY /voting-client-dotnet/ /app/voting-client-dotnet

WORKDIR /app/poll-summary-server/server
RUN dotnet build -c release


FROM mcr.microsoft.com/dotnet/core/aspnet:3.1


COPY --from=build /app/poll-summary-server/server/bin/release/netcoreapp3.1/ /app/

WORKDIR /app/
ENTRYPOINT [ "/app/server" ]