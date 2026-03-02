# APIPSI16 – REST API

This is the backend REST API for Xcelerate Links, built with **ASP.NET Core 8 Web API**.

---

## What it does

APIPSI16 exposes all data operations as HTTP JSON endpoints consumed by the MVC front-end (XcelerateLinks) and, optionally, any future mobile client. It also hosts the **SignalR** hub used for real-time chat.

---

## Project Structure

```
APIPSI16/
├── Controllers/           ← API controllers (one per domain entity)
├── Data/                  ← EF Core DbContext + partial extensions
├── DTOs/                  ← Data Transfer Objects (request/response shapes)
├── Filters/               ← Custom action filters (e.g. SwaggerFileUpload)
├── Hubs/                  ← SignalR ChatHub
├── Migrations/            ← EF Core database migrations
├── Models/                ← Entity classes (plain C# POCOs)
│   └── DTOs/              ← DTOs used only by the API
├── Program.cs             ← App composition root (services, middleware, routing)
├── Services/              ← Business logic services
│   ├── FileStorageService.cs   ← Saves/deletes uploaded files in wwwroot
│   ├── SessionService.cs       ← Validates active JWT sessions against the DB
│   ├── TokenService.cs         ← Creates and validates JWTs
│   ├── EmailSender.cs          ← Sends password-reset emails (SMTP)
│   └── UserService.cs          ← High-level user helpers
└── wwwroot/               ← Static files (served by UseStaticFiles)
    └── uploads/           ← Uploaded files (logos, profile pictures, banners)
```

---

## Authentication

### JWT Flow

1. Client POSTs `{ username, password }` to `POST /api/auth/login`.
2. `AuthController.Login` loads the user from the DB, verifies the password hash using ASP.NET Identity's `PasswordHasher<User>`.
3. If valid, `TokenService.GenerateToken` creates a **JWT** signed with the secret in `appsettings["Jwt:Key"]`.
4. A `Session` row is upserted in the `Sessions` table with the new token hash and expiry.
5. The JWT is returned in the response body.

### Session validation (`SessionService`)

Every API controller that extends `ApiControllerBase` calls `ValidateSessionAsync()` on sensitive endpoints. This checks:
- The JWT `NameIdentifier` claim resolves to a real user.
- A matching, non-expired row exists in the `Sessions` table.

This lets admins force-expire sessions without waiting for the JWT to expire naturally.

### Roles

Roles are stored as an `int` in the `Users` table and added to the JWT as a `ClaimTypes.Role` claim.

| Value | Name | Description |
|-------|------|-------------|
| `0` | Admin | Full access |
| `1` | Candidate | Default; applies to jobs, connects with others |
| `2` | Employer | Creates companies, posts jobs, views applicants |

---

## Main Controllers

| Controller | Route | Purpose |
|---|---|---|
| `AuthController` | `/api/auth` | Login, register, password reset, token refresh |
| `UsersController` | `/api/users` | User CRUD, profile, skills, job preferences, lookups |
| `CompaniesController` | `/api/companies` | Company CRUD, logo upload, member management |
| `OpportunitiesController` | `/api/opportunities` | Job posting CRUD, browse/search/filter |
| `JobApplicationsController` | `/api/jobapplications` | Apply, withdraw, pipeline status |
| `ConnectionsController` | `/api/connections` | Connect/disconnect, network list |
| `ChatController` | `/api/chat` | Create chats, messages, mark as read, conversations |
| `ChatHub` | `/hubs/chat` | **SignalR** real-time messaging hub |
| `RatingsController` | `/api/ratings` | Rate users/companies, upsert, aggregate stats |
| `NotificationsController` | `/api/notifications` | In-app notifications |
| `PostsController` | `/api/posts` | Social feed posts, comments, reactions |
| `SkillsController` | `/api/skills` | Skill lookup for profile |

---

## Real-Time Chat (SignalR)

### What is SignalR?

SignalR is an ASP.NET Core library that establishes a **persistent bidirectional connection** between the browser and the server. It prefers **WebSockets** but automatically falls back to Server-Sent Events or Long Polling.

In contrast to regular HTTP (request → response → close), a SignalR connection stays open so that **either side can push data at any moment**.

### ChatHub internals

`Hubs/ChatHub.cs` contains the server-side hub. Key concepts:

**Groups**  
Each conversation is a SignalR *Group* named `chat_{chatId}`. When a user opens a chat window the client calls `hub.invoke('JoinChat', chatId)`, adding their connection to that group. A message broadcast to the group reaches all current members.

Each user also has a private group `user_{userId}` used for personal notifications (read receipts, etc.).

**Hub methods (browser → server)**

| Method | What happens |
|--------|--------------|
| `JoinChat(chatId)` | Verifies participation, adds connection to `chat_N` group |
| `LeaveChat(chatId)` | Removes from group |
| `SendMessage(chatId, text)` | Persists `ChatMessage` to DB; broadcasts `ReceiveMessage` to group |
| `TypingIndicator(chatId, isTyping)` | Sends `UserTyping` event to others in group |
| `MarkMessageAsRead(chatId, msgId)` | Updates `ReadAt` in DB; notifies sender via `MessageRead` |

**Server push events (server → browser)**

| Event | Meaning |
|-------|---------|
| `ReceiveMessage` | A new message arrived |
| `UserTyping` | Someone is typing |
| `MessageRead` | Your message was read |
| `Error` | Hub rejected the action |

**Authentication**  
`ChatHub` carries `[Authorize]`. SignalR WebSockets can't send custom headers, so the JWT must be passed as a query parameter (`?access_token=…`). The SignalR JS client does this automatically when you provide `accessTokenFactory`:

```javascript
const conn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/chat', {
        accessTokenFactory: () => getTokenFromCookie()
    })
    .withAutomaticReconnect()
    .build();
```

---

## Database & Entity Framework

**DbContext:** `xcleratesystemslinks_SampleDBContext` (generated by EF Core scaffold from SQL Server, with partial class extensions for manually added tables).

**Key tables:**

| Table | Description |
|-------|-------------|
| `Users` | User accounts, profile data, hashed password |
| `Sessions` | Active JWT sessions (used for forced logout) |
| `Companies` | Company profiles |
| `CompanyMembers` | User ↔ Company membership with title/role |
| `Opportunities` | Job postings |
| `JobApplications` | Applications + status pipeline |
| `Connections` | Professional connections (pending/accepted/declined) |
| `Chats` / `ChatUsers` / `ChatMessages` | Messaging |
| `Ratings` | Star ratings (1-5) + review text for Users and Companies |
| `Posts` / `PostComments` / `PostReactions` | Social feed |
| `Notifications` | In-app notifications |
| `JobRoles` / `UserJobPreferences` | Job role lookup + user preferences |
| `ProfileEducation` / `ProfileExperience` | CV sections |
| `UserSkills` / `Skills` | Skills + endorsements |

**Migrations:**  
The primary migration is `AddSessionsTable`. Additional schema columns (e.g. `RequiredJobRoleIds` on Opportunities) were added via subsequent migrations.

Run migrations:
```bash
dotnet ef database update --context xcleratesystemslinks_SampleDBContext
```

---

## File Storage (`FileStorageService`)

Uploaded files (company logos, profile pictures, banners) are saved to `wwwroot/uploads/{folder}/{guid}.{ext}`. The stored path is relative (e.g. `/uploads/companies/abc123.jpg`) so it can be served as a static file by `UseStaticFiles()`.

**Validation** — only `.jpg`, `.jpeg`, `.png`, `.gif`; max 5 MB.

> **Note for deployment**: in a two-process setup (API + MVC running on different ports), logo files are now saved by the MVC app's `FileStorageService` directly into the MVC `wwwroot`, so the browser can always load them from the MVC origin. The API's `/api/companies/{id}/upload-logo` endpoint is still available for direct API callers.

---

## Running this project

```bash
# From APIPSI16_4/
dotnet run --project APIPSI16/APIPSI16.csproj
# API available at https://localhost:7263
# Swagger UI at https://localhost:7263/swagger
```

Configure `appsettings.json`:
```json
{
  "ConnectionStrings": { "DefaultConnection": "Server=...;..." },
  "Jwt": { "Key": "...", "Issuer": "xcelerate-links-api", "Audience": "xcelerate-links-clients", "ExpireMinutes": 1440 }
}
```
