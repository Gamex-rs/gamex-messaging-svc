# Gamex — Messaging Service Specification

> **Repository:** `gamex-rs/gamex-messaging-svc`  
> **Version:** 1.0  
> **Date:** 2026-05-12

---

## 1. Purpose

The Messaging Service manages direct conversations between users. Each conversation is always tied to a specific listing, giving both parties context about what is being discussed. It provides a REST API for conversation history and real-time message delivery via SignalR WebSockets. When a new message is sent, it publishes an event to SQS so the Notification Service can notify the recipient.

---

## 2. Tech Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 |
| Framework | ASP.NET Core Web API + SignalR |
| ORM | Entity Framework Core 8 |
| Database | MySQL 8 (Aurora MySQL Serverless v2 on AWS, official Docker image locally) |
| Real-time | SignalR (WebSocket with long-polling fallback) |
| SQS Publisher | AWSSDK.SQS |
| Auth | JWT RS256 — validated using public key from SSM (issued by User Library Service) |
| Config / Secrets | AWS SSM Parameter Store + Secrets Manager (env vars locally) |

---

## 3. API Endpoints

Base path: `/api/v1`

All endpoints require a valid Bearer JWT.

### 3.1 Conversations

| Method | Path | Description |
|---|---|---|
| `GET` | `/conversations` | List own conversations, ordered by latest message |
| `POST` | `/conversations` | Start a new conversation tied to a listing |
| `GET` | `/conversations/{id}` | Conversation detail with metadata |
| `PUT` | `/conversations/{id}/read` | Mark all messages in conversation as read |

#### `GET /conversations` — Response

```json
{
  "items": [
    {
      "id": "uuid",
      "listingId": "uuid",
      "gameTitle": "string",
      "otherUser": {
        "id": "uuid",
        "displayName": "string",
        "avatarUrl": "string"
      },
      "lastMessage": {
        "body": "string",
        "sentAt": "2026-05-12T10:00:00Z",
        "isOwn": true
      },
      "unreadCount": 2
    }
  ],
  "nextCursor": "string"
}
```

#### `POST /conversations` — Request

```json
{
  "listingId": "uuid",
  "initialMessage": "string"
}
```

- Returns `409` if a conversation for this `(listingId, currentUserId)` pair already exists — returns the existing conversation instead.
- The listing owner cannot start a conversation with themselves.

### 3.2 Messages

| Method | Path | Description |
|---|---|---|
| `GET` | `/conversations/{id}/messages` | Paginated message history (cursor-based, newest first) |
| `POST` | `/conversations/{id}/messages` | Send a message (also delivered via SignalR) |

#### `POST /conversations/{id}/messages` — Request

```json
{
  "body": "string"
}
```

- Returns `403` if the authenticated user is not a participant in the conversation.
- Max message body length: 2000 characters.

#### `GET /conversations/{id}/messages` — Query Parameters

| Parameter | Description |
|---|---|
| `cursor` | Pagination cursor (message id) |
| `pageSize` | Default 30, max 100 |

---

## 4. SignalR Hub

### Hub URL

`/hubs/messaging`

### Authentication

JWT passed as query parameter `?access_token=<jwt>` (SignalR standard pattern for WebSocket connections that cannot set headers).

### Client Groups

On connect, each authenticated user is automatically added to a personal group named `user:{userId}`. The server pushes messages to this group.

### Server → Client Events

| Event name | Payload | When sent |
|---|---|---|
| `MessageReceived` | Full message object + conversationId | New message arrives for user |
| `ConversationRead` | `{ conversationId }` | Other party marked conversation as read |

### Client → Server Methods

Clients send messages via the REST API, not via the hub. The hub is receive-only for the client. This keeps message persistence simple and avoids duplicate handling.

---

## 5. Data Model

### `conversations`

| Column | Type | Notes |
|---|---|---|
| `id` | CHAR(36) PK | UUID |
| `listing_id` | CHAR(36) | Reference to listing in User Library Service (no FK across services) |
| `listing_game_title` | VARCHAR(255) | Snapshot for display in conversation list |
| `initiator_id` | CHAR(36) | User who started the conversation |
| `recipient_id` | CHAR(36) | Listing owner |
| `created_at` | DATETIME | |
| UNIQUE | `(listing_id, initiator_id)` | One conversation per user per listing |

### `messages`

| Column | Type | Notes |
|---|---|---|
| `id` | CHAR(36) PK | UUID |
| `conversation_id` | CHAR(36) FK → conversations.id | |
| `sender_id` | CHAR(36) | |
| `body` | TEXT | Not null, max 2000 chars |
| `read_at` | DATETIME | Null = unread |
| `created_at` | DATETIME | |

Index on `(conversation_id, created_at DESC)` for efficient history pagination.

---

## 6. SQS Event Publishing

### Queue: `gamex-activity-events`

Published when a new message is sent.

**Event: `message.sent`**

```json
{
  "eventType": "message.sent",
  "conversationId": "uuid",
  "messageId": "uuid",
  "senderId": "uuid",
  "recipientId": "uuid",
  "occurredAt": "2026-05-12T10:00:00Z"
}
```

The message body is intentionally omitted from the event payload — the Notification Service only needs to know that a message arrived, not its content.

---

## 7. Error Handling

All error responses follow RFC 7807 Problem Details:

```json
{
  "type": "https://gamex.rs/errors/forbidden",
  "title": "Access denied",
  "status": 403,
  "detail": "You are not a participant in this conversation"
}
```

---

## 8. Local Development

```yaml
# docker-compose.yml
services:
  messaging-svc:
    build: .
    ports:
      - "5003:8080"
    environment:
      - DB_CONNECTION_STRING=Server=messaging-db;Database=messaging_db;User=root;Password=localdev;
      - JWT_PUBLIC_KEY=${JWT_PUBLIC_KEY}
      - SQS_ACTIVITY_EVENTS_URL=http://localstack:4566/000000000000/gamex-activity-events
    depends_on:
      - messaging-db

  messaging-db:
    image: mysql:8.0
    environment:
      MYSQL_DATABASE: messaging_db
      MYSQL_ROOT_PASSWORD: localdev
    ports:
      - "3309:3306"
    volumes:
      - messaging-db-data:/var/lib/mysql

volumes:
  messaging-db-data:
```

---

## 9. CI/CD

| Workflow | Trigger | Action |
|---|---|---|
| `ci.yml` | Push / PR | Build + unit + integration tests |
| `deploy-dev.yml` | Merge to `main` | Build image → ECR → deploy to `gx-dev` ECS |
| `deploy-prod.yml` | Tag `v*.*.*` | Manual approval → ECR → deploy to `gx-prod` ECS |

---

## 10. Environment Variables Reference

| Variable | Description | Source (AWS) |
|---|---|---|
| `DB_CONNECTION_STRING` | MySQL connection string | Secrets Manager |
| `JWT_PUBLIC_KEY` | RS256 public key (PEM) | SSM Parameter Store |
| `SQS_ACTIVITY_EVENTS_URL` | Queue URL for notification events | SSM Parameter Store |
| `ASPNETCORE_ENVIRONMENT` | `Development` / `Production` | ECS task definition |

---

## 11. Implementation Notes (2026-10-24)

Decisions taken while implementing; they refine the sections above.

### 11.1 Starting a conversation without a synchronous call

The service has no listing data and must not call the User Library Service. `POST /conversations` therefore takes the **contact token** the User Library Service returns from `GET /listings/{id}`:

```json
{ "listingId": "uuid", "contactToken": "<jwt>", "initialMessage": "string" }
```

The token is an RS256 JWT signed with the User Library key (`typ` `gx-contact+jwt`, `aud` `gamex-listing-contact`, 24 h lifetime) carrying `listing_id`, `owner_id`, `owner_name`, `owner_avatar`, `game_title`. It is verified with `JWT_PUBLIC_KEY`, so callers cannot invent a listing owner. An access token is not accepted as a contact token (different audience and type). `listingId` is optional but must match the token when sent.

- Owner messaging their own listing → `400`. Invalid/expired token → `400` (`contactToken`).
- Existing conversation for `(listingId, caller)` → `409` whose problem body includes `conversation` (the existing conversation).
- Success → `201` with the conversation.

### 11.2 Participants and names

`conversations` stores display-name snapshots: `initiator_name` from the JWT `name` claim, `recipient_name`/`recipient_avatar_url` from the contact token; a participant's name is refreshed whenever they post. Additional columns: `initiator_name`, `initiator_avatar_url` (always null for now: the JWT has no avatar), `recipient_name`, `recipient_avatar_url`, `last_message_at` (orders `GET /conversations`).

### 11.3 API details

- `GET /conversations?cursor&pageSize` (default 20, max 50) — newest activity first; `unreadCount` counts the other party's unread messages.
- `GET /conversations/{id}` → `{ id, listingId, gameTitle, otherUser, createdAt, unreadCount }`.
- `GET /conversations/{id}/messages` items: `{ id, conversationId, senderId, body, sentAt, readAt, isOwn }`; `nextCursor` is the id of the oldest message on the page.
- `PUT /conversations/{id}/read` → `204`; marks the other party's messages read and pushes `ConversationRead { conversationId }` to them (only if something changed).
- Message bodies are trimmed; 1–2000 characters. Sending (start + post) is limited per user (`SEND_RATE_LIMIT_PER_MINUTE`, default 30).
- Non-participants get `403` ("You are not a participant in this conversation"); unknown ids `404`.

### 11.4 SignalR

`MessageReceived` is pushed to **both** participants' groups (the sender's other devices stay in sync) with `{ id, conversationId, senderId, senderName, body, sentAt }`. Running several instances requires a SignalR backplane (e.g. Redis or Azure SignalR) — not configured yet.

### 11.5 `message.sent` payload

Enriched so the Notification Service needs no lookups: `eventType, conversationId, messageId, senderId, senderName, recipientId, listingId, gameTitle, occurredAt` (still no message body).

### 11.6 Additional environment variables

| Variable | Description |
|---|---|
| `JWT_ISSUER` / `JWT_AUDIENCE` | Expected `iss` / `aud` of access tokens (validated when set; the issuer also applies to contact tokens) |
| `SEND_RATE_LIMIT_PER_MINUTE` | Messages per user per minute (default 30) |
| `AWS_SERVICE_URL` / `AWS_DEFAULT_REGION` | LocalStack endpoint / region for local development |
| `DB_MIGRATE_ON_STARTUP` | Apply migrations at startup (default true in Development) |
