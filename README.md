# Xcelerate Links

A LinkedIn-inspired professional networking platform built with ASP.NET Core.  
Users can create profiles, post jobs, apply to opportunities, connect with other professionals, and message each other in real time.

---

## Table of Contents

1. [Project Structure](#1-project-structure)
2. [Architecture Overview](#2-architecture-overview)
3. [Backend API (APIPSI16)](#3-backend-api-apipsi16)
4. [Frontend MVC (XcelerateLinks)](#4-frontend-mvc-xceleratelinks)
5. [Key Features & How They Work](#5-key-features--how-they-work)
   - [Authentication & Sessions](#authentication--sessions)
   - [User Roles](#user-roles)
   - [Companies & Opportunities](#companies--opportunities)
   - [Job Applications](#job-applications)
   - [Connections (Network)](#connections-network)
   - [Ratings](#ratings)
6. [Real-Time Chat with SignalR](#6-real-time-chat-with-signalr)
   - [How SignalR works](#how-signalr-works)
   - [ChatHub explained](#chathub-explained)
   - [The React Chat Widget](#the-react-chat-widget)
7. [Translation System (PT ↔ EN)](#7-translation-system-pt--en)
   - [How it works](#how-it-works)
   - [Adding new translations](#adding-new-translations)
8. [Database & Entity Framework](#8-database--entity-framework)
9. [Running the Project Locally](#9-running-the-project-locally)
10. [Environment Variables](#10-environment-variables)

---

## 1. Project Structure

```
APIPSI16_4/
├── APIPSI16/            ← REST API (ASP.NET Core Web API)
│   ├── Controllers/     ← API endpoints (ChatController, UsersController, …)
│   ├── Data/            ← Entity Framework DbContext
│   ├── Hubs/            ← SignalR ChatHub
│   ├── Migrations/      ← EF Core database migrations
│   ├── Models/          ← Entity classes (User, Company, Opportunity, …)
│   │   └── DTOs/        ← Data Transfer Objects for API responses
│   └── Services/        ← FileStorageService, SessionService, TokenService
│
└── XcelerateLinks/      ← MVC web front-end (Razor views)
    ├── Controllers/     ← MVC controllers (talk to the API via HttpClient)
    ├── Views/           ← Razor .cshtml templates (all UI)
    │   ├── Shared/      ← _Layout.cshtml (nav, footer, theme, translation)
    │   │               ← _ChatWidget.cshtml (React floating chat bubble)
    │   ├── Account/     ← Login, Register, password reset
    │   ├── Users/       ← Profile, edit, network, employer requests
    │   ├── Opportunities/ ← Browse, create, edit job listings
    │   ├── Companies/   ← Company profiles, edit, manage
    │   ├── Applications/ ← Apply, track applications
    │   └── Chats/       ← Full-screen messages page
    ├── Services/        ← SessionService, token helpers
    └── wwwroot/         ← Static files (CSS, JS, images)
```

---

## 2. Architecture Overview

```
Browser
  │
  │  HTTP/HTTPS (Razor pages, form posts)
  ▼
XcelerateLinks MVC (port 7258)
  │  Cookie auth (ASP.NET Core Identity Cookies)
  │  HttpClient calls to the API (with JWT from cookie)
  │
  │  HTTP/HTTPS + WebSocket (SignalR)
  ▼
APIPSI16 REST API (port 7263)
  │  JWT Bearer auth
  ▼
SQL Server database (bsite.net hosted)
```

**Key insight:** The MVC app is a *thin proxy* — it authenticates users with cookies, stores the JWT token in a cookie (`ApiAccessToken`), and then proxies most requests to the REST API by attaching that JWT as a `Bearer` token. This means the API is the single source of truth and could be used by any other client (mobile app, etc.).

---

## 3. Backend API (APIPSI16)

### Controllers

| Controller | Route | Purpose |
|---|---|---|
| `AuthController` | `api/auth` | Login, register, password reset |
| `UsersController` | `api/users` | CRUD for users, profile, job preferences, lookups |
| `CompaniesController` | `api/companies` | CRUD for companies, logo upload, members |
| `OpportunitiesController` | `api/opportunities` | CRUD for job postings |
| `JobApplicationsController` | `api/jobapplications` | Apply, withdraw, pipeline |
| `ConnectionsController` | `api/connections` | Connect/disconnect, network |
| `ChatController` | `api/chat` | Create chats, get messages, mark as read |
| `ChatHub` | `/hubs/chat` | **SignalR** real-time messaging |
| `RatingsController` | `api/ratings` | Rate companies and users |
| `PostsController` | `api/posts` | Social feed posts |
| `NotificationsController` | `api/notifications` | Notifications |
| `SkillsController` | `api/skills` | Skill lookup |

### Authentication flow

1. User POSTs credentials to `POST api/auth/login`.
2. API validates password hash (ASP.NET Identity `PasswordHasher`), creates a `Session` row in the DB, and returns a **JWT**.
3. The MVC app stores the JWT in a **cookie** (`ApiAccessToken`) and signs in with cookie auth (so Razor `User.Identity.IsAuthenticated` works).
4. Subsequent requests: MVC's `TokenHandler` (a `DelegatingHandler`) reads the JWT from the cookie and adds `Authorization: Bearer <token>` on every outgoing `HttpClient` call to the API.
5. The API validates the JWT **and** checks the `Sessions` table (`SessionService.IsSessionValidAsync`) to support forced logout.

### JWT configuration

`appsettings.json` (API):
```json
"Jwt": {
  "Key": "...",
  "Issuer": "xcelerate-links-api",
  "Audience": "xcelerate-links-clients",
  "ExpireMinutes": 1440
}
```

---

## 4. Frontend MVC (XcelerateLinks)

### Controller pattern

Every MVC controller extends `ApiControllerBase` → `BaseController`.

```csharp
// ApiControllerBase creates an HttpClient with the JWT already attached:
protected HttpClient CreateAuthorizedClient()
{
    var client = _httpFactory.CreateClient("Api");
    if (Request.Cookies.TryGetValue("ApiAccessToken", out var token))
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return client;
}
```

A typical action looks like:

```csharp
public async Task<IActionResult> Index()
{
    if (!await ValidateSessionAsync()) return RedirectToAction("Login", "Account");
    var client = CreateAuthorizedClient();
    var resp = await client.GetAsync("api/opportunities");
    var data = await resp.Content.ReadFromJsonAsync<IEnumerable<Opportunity>>();
    return View(data);
}
```

### Session validation

`BaseController.ValidateSessionAsync()` checks if the JWT cookie is still valid on the API side. If not, it clears the cookie and forces a re-login. This prevents stale sessions from being usable after an admin force-logs someone out.

---

## 5. Key Features & How They Work

### Authentication & Sessions

- Passwords are stored hashed using **ASP.NET Core's `PasswordHasher<User>`** (PBKDF2 with HMACSHA256). The exact iteration count depends on the .NET version — .NET 8 defaults to 600,000 PBKDF2 iterations.
- Every login creates a row in the `Sessions` table. Only **one active session** per user is allowed — logging in invalidates all previous sessions.
- The `Sessions` table lets admins (or the server) invalidate sessions without waiting for the JWT to expire.

### User Roles

| Role value | Name | Description |
|---|---|---|
| `0` | Admin | Full access, approves employer requests, manages all data |
| `1` | Candidate | Default role, can apply to jobs, connect with others |
| `2` | Employer | Can create companies, post jobs, view applicants |

Role is stored as `int` on the `User` model and included as a `ClaimTypes.Role` in the JWT.

### Companies & Opportunities

- **Companies** are created by Employers or Admins. A company has members (`CompanyMembers` table linking users to companies with a title).
- **Opportunities** belong to a company. They have `EmploymentType`, `SeniorityLevel`, `RemoteOption`, and `RequiredJobRoleIds` (comma-separated job role IDs).
- A company's **logo** is uploaded via `POST api/companies/{id}/upload-logo`. The file is saved to `wwwroot/uploads/companies/` as a GUID-named file.

### Job Applications

`JobApplications` track the full hiring pipeline:

```
Submitted → Under Review → Interview → Offer → Accepted / Rejected
```

Stored as a `byte` (`Status` field, 0–5).

Employers see all applications to their companies via the Pipeline view (`/Applications/Pipeline`).

### Connections (Network)

The `Connections` table has `RequesterUserId`, `AddresseeUserId`, and `Status` (0 = Pending, 1 = Accepted, 2 = Declined). The network page shows users that are **not already connected** to the current user.

### Ratings

Any authenticated user can rate a Company or User (1–5 stars + optional review text). Ratings are stored in the `Ratings` table with `EntityType` ("User" or "Company") and `RatedEntityId`. Each user can only rate each entity once.

---

## 6. Real-Time Chat with SignalR

### How SignalR works

**SignalR** is a library that maintains a **persistent bidirectional connection** between the browser and the server. It prefers **WebSockets** but falls back to Server-Sent Events or Long Polling if WebSockets are unavailable.

Normal HTTP: client asks → server replies → connection closes.  
SignalR: client connects → connection stays open → **either side can push data** at any time.

In this project, the API exposes the SignalR hub at `/hubs/chat`.

```
Browser ←──WebSocket──→ ChatHub (server)
            ↑
    SignalR keeps this open
    Both sides can call methods on each other
```

### ChatHub explained

The `ChatHub` in `APIPSI16/Hubs/ChatHub.cs` is the server-side hub.

**Groups:** Each chat room is a SignalR **Group** named `chat_{chatId}`. When a user opens a conversation, the client calls `hub.invoke('JoinChat', chatId)`, which adds their connection to that group. Any message sent to the group is received by all members.

Each user also has a personal group `user_{userId}` for direct notifications (e.g., "message read" receipts).

**Hub methods (called by the client):**

| Client calls | Server does |
|---|---|
| `JoinChat(chatId)` | Adds connection to `chat_N` group after verifying participation |
| `LeaveChat(chatId)` | Removes from group |
| `SendMessage(chatId, text)` | Saves to DB, broadcasts `ReceiveMessage` to the group |
| `TypingIndicator(chatId, isTyping)` | Broadcasts `UserTyping` to others in the group |
| `MarkMessageAsRead(chatId, msgId)` | Updates `ReadAt`, notifies sender via `MessageRead` |

**Server pushes (received by the client):**

| Server sends | Meaning |
|---|---|
| `ReceiveMessage` | New message arrived in a chat |
| `UserTyping` | Someone is typing |
| `MessageRead` | Your message was read |
| `Error` | Something went wrong |

### The React Chat Widget

The floating chat bubble in the bottom-right corner is a **React component** embedded in `Views/Shared/_ChatWidget.cshtml`.

It is written in **plain React without JSX** (because there's no build step — the script runs directly in the browser). Instead of `<div className="foo">`, you write `React.createElement('div', { className: 'foo' }, children)`.

**How it's loaded:**

```html
<!-- React and ReactDOM are loaded from CDN in _Layout.cshtml -->
<script src="https://unpkg.com/react@18/umd/react.production.min.js"></script>
<script src="https://unpkg.com/react-dom@18/umd/react-dom.production.min.js"></script>
<!-- SignalR client also from CDN -->
<script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js"></script>
```

The component is mounted in `_ChatWidget.cshtml` at the bottom of every page (except the Messages page and auth pages):

```javascript
const root = ReactDOM.createRoot(document.getElementById('chat-widget-root'));
root.render(React.createElement(ChatWidget));
```

**Component state:**

| State | Meaning |
|---|---|
| `open` | Whether the chat panel is visible |
| `convs` | List of conversations from `GET api/chat` |
| `activeChatId` | Which conversation is currently open |
| `messages` | Messages in the active conversation |
| `input` | Current text being typed |
| `unreadTotal` | Total unread badge count |

**SignalR connection in the widget:**

```javascript
const conn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/chat', {
        accessTokenFactory: () => getToken() // reads JWT from cookie
    })
    .withAutomaticReconnect()
    .build();

conn.on('ReceiveMessage', (msg) => {
    if (msg.chatId === activeChatRef.current) {
        setMessages(prev => [...prev, msg]); // append to current chat
    }
    loadConvs(); // refresh conversation list for unread counts
});

conn.start();
```

The `accessTokenFactory` is critical: SignalR's WebSocket connection cannot send custom headers, so the JWT must be passed as a query parameter `?access_token=...`. The SignalR client handles this automatically when you provide `accessTokenFactory`.

**Why React for the widget?**

The chat widget needs to:
- Maintain multiple pieces of state (conversations, active chat, messages, input, unread counts)
- Re-render efficiently when real-time messages arrive
- Handle events (click, keypress, WebSocket messages) simultaneously

Managing all of this with plain jQuery would be complex and error-prone. React's **declarative model** — describe what the UI should look like for any given state, and React updates the DOM — makes this much cleaner.

---

## 7. Translation System (PT ↔ EN)

### How it works

The translation system is purely **client-side JavaScript** in `_Layout.cshtml`. There is no server-side localization — the server always renders Portuguese text, and the JS replaces it on click.

**The language toggle button** (`#langToggle`) is in the navigation bar. When clicked, it calls `applyLang('en')` (or `'pt'` to restore).

**Core mechanism — `TreeWalker`:**

Instead of querying specific HTML elements (which was unreliable), the system uses the browser's `TreeWalker` API to visit **every text node** in `document.body`:

```javascript
var walker = document.createTreeWalker(
    document.body,
    NodeFilter.SHOW_TEXT,  // only text nodes, not elements
    {
        acceptNode: function(node) {
            var tag = node.parentElement?.tagName;
            // Skip <script>, <style>, and the toggle buttons themselves
            if (tag === 'SCRIPT' || tag === 'STYLE') return NodeFilter.FILTER_REJECT;
            if (node.parentElement?.id === 'langToggle') return NodeFilter.FILTER_REJECT;
            return NodeFilter.FILTER_ACCEPT;
        }
    }
);
```

A text node is a raw text chunk in the DOM. For example, `<h1>Oportunidades</h1>` has one text node with value `"Oportunidades"`. For `<h1>💼 Oportunidades</h1>`, the text node value is `"💼 Oportunidades"`.

**Translation lookup (two-pass):**

```javascript
function translateTextNode(node, dict) {
    var text = node.textContent;
    var trimmed = text.trim();

    // Pass 1: exact match on trimmed text
    if (dict[trimmed]) {
        node.textContent = text.replace(trimmed, dict[trimmed]);
        return;
    }

    // Pass 2: substring replacement (longest keys first to avoid partial clobbers)
    var keys = Object.keys(dict).sort((a, b) => b.length - a.length);
    var newText = text;
    var changed = false;
    for (var key of keys) {
        if (newText.includes(key)) {
            newText = newText.split(key).join(dict[key]);
            changed = true;
        }
    }
    if (changed) node.textContent = newText;
}
```

Pass 1 handles exact matches (most elements).  
Pass 2 handles text nodes that contain multiple translatable phrases (e.g., a paragraph with embedded strings).

**Placeholders** are handled separately because they are attributes, not text nodes:

```javascript
document.querySelectorAll('[placeholder]').forEach(el => {
    if (dict[el.placeholder]) el.placeholder = dict[el.placeholder];
});
```

**`data-i18n` attribute (explicit key):**  
Any element can opt into an explicit translation key:

```html
<span data-i18n="Oportunidades">Oportunidades</span>
```

The JS will always translate `data-i18n` elements by their attribute value, even if the text node match fails. This is useful for dynamically generated content.

**Restoring Portuguese:**

Every modified node's original value is stored in a `Map`:

```javascript
var saved = new Map(); // node → originalValue

// On translate: saved.set(node, originalValue)
// On restore (PT): saved.forEach((orig, node) => node.textContent = orig); saved.clear();
```

### Adding new translations

Open `Views/Shared/_Layout.cshtml` and find the `var PT_EN = { ... }` dictionary.  
Add your entry:

```javascript
'Texto em português': 'Text in English',
```

The key is the **exact Portuguese text** as it appears in the rendered HTML (including any emoji prefix if present, e.g. `'💼 Oportunidades': 'Opportunities'`). Whitespace at the start/end is automatically trimmed before lookup.

**Tips:**
- For emoji prefixes like `💼 Texto`, add both `'💼 Texto': '💼 English'` AND `'Texto': 'English'` — the substring pass will handle both.
- For elements where text is known to vary (dynamic values), use `data-i18n="key"` on the element.

---

## 8. Database & Entity Framework

The database is a **SQL Server** instance (hosted on bsite.net for the deployed version; LocalDB for local development).

The `xcleratesystemslinks_SampleDBContext` is the main EF Core DbContext. It uses a **database-first** approach for the main tables (scaffolded from the existing DB), with **code-first migrations** for new tables added during development.

### Key tables

| Table | Description |
|---|---|
| `Users` | User accounts (credentials, profile data) |
| `Sessions` | Active JWT sessions (one per user; used for forced logout) |
| `Companies` | Company profiles |
| `CompanyMembers` | Links users to companies with a role/title |
| `Opportunities` | Job postings |
| `JobApplications` | Applications from candidates to opportunities |
| `Connections` | Professional connections between users |
| `Chats` / `ChatUsers` / `ChatMessages` | Messaging |
| `Ratings` | Star ratings + reviews for users and companies |
| `Posts` / `PostComments` / `PostReactions` | Social feed |
| `Notifications` | In-app notifications |
| `JobRoles` | Lookup table for job role/area names |
| `Nationalities` | Lookup table for nationality names |
| `UserSkills` / `Skills` / `SkillEndorsements` | Skills system |
| `UserJobPreferences` | Many-to-many: user ↔ job roles |
| `ProfileEducation` / `ProfileExperience` | CV sections |

### Migrations

There is one large initial migration (`AddSessionsTable`) and a smaller follow-up (`AddRequiredJobRoleIdsToOpportunity`). Run migrations with:

```bash
cd APIPSI16_4/APIPSI16
dotnet ef database update --context xcleratesystemslinks_SampleDBContext
```

---

## 9. Running the Project Locally

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- SQL Server (LocalDB is fine for local dev, included with Visual Studio)

### Steps

**1. Clone the repository**

```bash
git clone <repo-url>
cd xceleratesystemslinksagain/APIPSI16_4
```

**2. Configure the API**

Edit `APIPSI16/appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=XcelerateLinksDev;Trusted_Connection=True;"
  }
}
```

**3. Apply migrations & seed the database**

```bash
cd APIPSI16
dotnet ef database update --context xcleratesystemslinks_SampleDBContext
```

**4. Run the API**

```bash
cd APIPSI16
dotnet run
# API starts on https://localhost:7263
```

**5. Run the MVC front-end**

In a separate terminal:
```bash
cd XcelerateLinks
dotnet run
# MVC starts on https://localhost:7258
```

**6. Open the app**

Navigate to `https://localhost:7258` in your browser.

---

## 10. Environment Variables

The app uses these environment variables in production (override appsettings.json):

| Variable | Used by | Description |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | Both | Full SQL Server connection string |
| `Api__BaseUrl` | MVC | Base URL of the API (e.g. `https://api.example.com`) |
| `ASPNETCORE_URLS` | API | Override listen addresses |
| `ASPNETCORE_ENVIRONMENT` | Both | `Development` or `Production` |

---

## File Upload

Company logos and user profile pictures/banners are stored in `wwwroot/uploads/` on the server. The path is saved in the database (e.g. `/uploads/companies/abc123.jpg`). In production, this folder should be on persistent storage or replaced with a cloud blob store (Azure Blob, S3, etc.).

**Allowed file types:** `.jpg`, `.jpeg`, `.png`, `.gif`  
**Max file size:** 5 MB

---

## Tech Stack Summary

| Layer | Technology |
|---|---|
| Backend API | ASP.NET Core 8 Web API |
| Frontend | ASP.NET Core 8 MVC + Razor Views |
| Database ORM | Entity Framework Core 8 |
| Database | SQL Server |
| Real-time | SignalR (WebSockets) |
| Authentication | JWT Bearer (API) + Cookie (MVC) |
| UI framework | Bootstrap 5 + custom CSS |
| Interactive UI | React 18 (CDN, no build step, no JSX) |
| Chat client lib | @microsoft/signalr 8.0 (CDN) |
