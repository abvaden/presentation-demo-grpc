FROM node:alpine

WORKDIR /app/
COPY user-server/package.json /app/user-server/package.json
COPY user-server/package-lock.json /app/user-server/package-lock.json

WORKDIR /app/user-server
RUN npm ci

COPY protos/ /app/protos/
COPY user-server/src /app/user-server/src

CMD ["node", "/app/user-server/src/index.js"]