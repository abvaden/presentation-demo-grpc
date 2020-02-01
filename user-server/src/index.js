


const grpc = require('grpc');
const users_proto = require('./proto');
const userService = require('./user-service');



const port = process.env.PORT || 50000;


var server;
function main() {
  server = new grpc.Server();
  server.addService(users_proto.Users.service, {
    CreateUser: userService.createUser,
    UserExists: userService.userExists,
    ValidateUser: userService.validateUser,
  });
  server.bind(`0.0.0.0:${port}`, grpc.ServerCredentials.createInsecure());
  console.log(`Listening for connection on ${port}`);
  server.start();
}

const shutdown = function() {
  server.tryShutdown(() => {
    process.exit(0);
  })
}

process.on('SIGILL', shutdown);
process.on('SIGTERM', shutdown);
process.on('SIGTRAP', shutdown);
process.on('SIGINT', shutdown);

main();