# XcelerateLinks – MVC Front-end

This is the web front-end for Xcelerate Links, built with **ASP.NET Core 8 MVC + Razor Views**.

---

## What it does

XcelerateLinks is a LinkedIn-style professional networking platform. It renders HTML pages, handles cookie-based authentication, and communicates with the APIPSI16 REST API (or, in a single-process deployment, the API controllers in the same process via the shared project reference).

---

## Project Structure

```
XcelerateLinks/
├── Controllers/           ← MVC controllers (thin proxies to the API)
│   ├── BaseController.cs       ← Base class with ValidateSessionAsync, helpers
│   ├── ApiControllerBase.cs    ← Adds CreateAuthorizedClient (HttpClient with JWT)
│   ├── AccountController.cs    ← Login, register, password reset (sets cookie)
│   ├── UsersController.cs      ← Profiles, edit, network, employer requests
│   ├── CompaniesController.cs  ← Company CRUD, logo upload (saves to local wwwroot)
│   ├── OpportunitiesController.cs ← Browse, create, edit, delete job listings
│   ├── ApplicationsController.cs  ← Apply, track, pipeline
│   ├── ChatsController.cs      ← Chat index, create, full-screen messages
│   └── ConnectionsController.cs   ← Connect, network
├── Models/
│   └── ViewModels/        ← ViewModels for complex form pages
├── Services/
│   ├── TokenHandler.cs        ← DelegatingHandler: attaches JWT to every API call
│   ├── SessionService.cs      ← Server-side session validation
│   ├── ApiClient.cs           ← Typed HttpClient for generic API calls
│   └── UsersApiClient.cs      ← Typed client for user-specific API calls
├── Views/
│   ├── Shared/
│   │   ├── _Layout.cshtml     ← Master layout: nav, footer, theme toggle, language toggle, chat widget mount point
│   │   ├── _ChatWidget.cshtml ← Floating React chat bubble (injected into every page)
│   │   └── _UserMenu.cshtml   ← User avatar dropdown
│   ├── Account/           ← Login, Register, password reset pages
│   ├── Users/             ← Profile, edit, network, employer requests
│   ├── Companies/         ← Company explorer, edit, manage, details
│   ├── Opportunities/     ← Browse, create, edit, details
│   ├── Applications/      ← Apply form, my applications tracker, employer pipeline
│   ├── Chats/             ← Chat list, create, full-screen messages
│   ├── Connections/       ← My connections
│   └── Home/              ← Landing page, admin dashboard
├── wwwroot/               ← Static files served by the MVC app
│   ├── css/               ← Custom stylesheets
│   ├── js/                ← Custom scripts
│   ├── images/            ← Logos, default images
│   └── uploads/           ← User-uploaded files (logos, avatars, banners)
└── Program.cs             ← App composition root
```

---

## Architecture: Thin MVC Proxy

The MVC app does not contain business logic. Each controller action:

1. Validates the user session (`ValidateSessionAsync()`).
2. Creates an `HttpClient` with the JWT attached (`CreateAuthorizedClient()`).
3. Calls the corresponding API endpoint.
4. Passes the response data to a Razor view.

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

### TokenHandler

`Services/TokenHandler.cs` is a `DelegatingHandler` registered on the `"Api"` named HttpClient. It reads the JWT from the `ApiAccessToken` cookie and adds `Authorization: Bearer <token>` to every outgoing request automatically. Controllers don't need to set the header themselves.

### Session validation

`BaseController.ValidateSessionAsync()` calls `GET /api/auth/validate-session` with the current JWT. If the session was invalidated (e.g. admin forced logout), it clears the cookie and redirects to login.

---

## Cookie Authentication

On login success:
1. `AccountController.Login` calls `POST /api/auth/login`, receives a JWT.
2. The JWT is stored in cookie `ApiAccessToken` (HttpOnly, Lax SameSite).
3. `SignInAsync` is called with a `ClaimsPrincipal` built from the JWT claims, so `User.Identity.IsAuthenticated` and `User.FindFirst(...)` work in Razor views.
4. On logout, both the identity cookie and `ApiAccessToken` are cleared.

---

## React Chat Widget (`_ChatWidget.cshtml`)

The floating chat bubble in the bottom-right corner is a **React component** rendered without a build step.

### Why React?

The widget maintains multiple pieces of state simultaneously:
- Open/closed panel state
- List of conversations (polled + SignalR push)
- Active conversation and its messages
- Input text, unread badge count

Managing this with plain jQuery or vanilla JS event handlers would be complex and brittle. React's **declarative model** (describe what the UI looks like for any given state; React handles DOM updates) makes it clean and maintainable.

### No build step — `React.createElement` instead of JSX

JSX (`<div className="foo">...</div>`) requires a transpiler (Babel/TypeScript). Since this project has no frontend build pipeline, the widget uses the **UMD browser builds** of React loaded from CDN:

```html
<script src="https://unpkg.com/react@18/umd/react.production.min.js"></script>
<script src="https://unpkg.com/react-dom@18/umd/react-dom.production.min.js"></script>
```

Instead of JSX, you call `React.createElement` directly:

```javascript
// JSX equivalent:  <div className="cw-panel">...</div>
React.createElement('div', { className: 'cw-panel' }, ...children)
```

### Component state

| State | Type | Description |
|-------|------|-------------|
| `open` | bool | Whether the chat panel is showing |
| `convs` | array | Conversations from `GET /api/chat/conversations` |
| `activeChatId` | number\|null | Which conversation is open |
| `messages` | array | Messages in the active conversation |
| `input` | string | Current text being typed |
| `unreadTotal` | number | Sum of unread messages across all chats |

### SignalR in the widget

```javascript
const conn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/chat', {
        accessTokenFactory: () => getTokenFromCookie()
    })
    .withAutomaticReconnect()
    .build();

conn.on('ReceiveMessage', (msg) => {
    if (msg.chatId === activeChatRef.current) {
        setMessages(prev => [...prev, msg]);
    }
    loadConvs(); // refresh unread counts
});
conn.start();
```

**Why `accessTokenFactory`?**  
WebSocket handshakes cannot include custom HTTP headers. The SignalR JS client appends the token as `?access_token=…` in the WebSocket upgrade request. The server's `[Authorize]` hub reads it from the query string.

**`useRef` for real-time correctness**  
`activeChatRef` is a `useRef` that mirrors the `activeChatId` state. Inside `conn.on('ReceiveMessage', ...)`, a closure captures `activeChatRef` rather than the state variable, ensuring it always reads the *current* value even inside stale closures.

### Mounting

```javascript
if (typeof React !== 'undefined' && typeof ReactDOM !== 'undefined') {
    const root = ReactDOM.createRoot(document.getElementById('chat-widget-root'));
    root.render(React.createElement(ChatWidget));
}
```

The `chat-widget-root` div is placed in `_ChatWidget.cshtml` (partial included by `_Layout`). The guard `typeof React !== 'undefined'` prevents errors on pages where the CDN scripts fail to load.

---

## Translation System (PT ↔ EN)

The translation system is fully **client-side JavaScript** in `_Layout.cshtml`. The server always renders Portuguese; JS swaps text on demand.

### The toggle button

```html
<button id="langToggle">EN</button>
```

Clicking it calls `applyLang('en')` (or `'pt'` to restore). The label shows the *other* language (what you'll switch TO).

### How translations work — TreeWalker

The old approach used `querySelectorAll('a, button, span, …')` and matched `textContent` exactly. It failed for:
- Emoji-prefixed text (`💼 Oportunidades` ≠ `Oportunidades`)
- Razor-rendered whitespace padding (`"\n  Cancelar\n"`)
- Elements with mixed child nodes

The current approach uses the browser's `createTreeWalker` to walk **every raw text node** in `<body>`:

```javascript
var walker = document.createTreeWalker(
    document.body,
    NodeFilter.SHOW_TEXT,
    {
        acceptNode: function(node) {
            var tag = node.parentElement?.tagName;
            if (tag === 'SCRIPT' || tag === 'STYLE') return NodeFilter.FILTER_REJECT;
            if (node.parentElement?.id === 'langToggle') return NodeFilter.FILTER_REJECT;
            return NodeFilter.FILTER_ACCEPT;
        }
    }
);
```

A **text node** is the raw text chunk inside an element. `<h1>💼 Oportunidades</h1>` has one text node with value `"💼 Oportunidades"`.

### Two-pass translation per text node

```javascript
function translateTextNode(node, dict) {
    var trimmed = node.textContent.trim();
    // Pass 1: exact match after trimming
    if (dict[trimmed] !== undefined) { node.textContent = ...; return; }
    // Pass 2: substring replacement (longest keys first)
    var keys = Object.keys(dict).sort((a,b) => b.length - a.length);
    var newText = node.textContent;
    for (var key of keys) {
        if (newText.includes(key)) newText = newText.split(key).join(dict[key]);
    }
    if (newText !== node.textContent) node.textContent = newText;
}
```

**Pass 1** handles the majority of elements (exact match after whitespace trim).  
**Pass 2** handles text nodes containing multiple translatable phrases, or emoji-prefixed text that didn't match the exact key.  
Sorting by descending key length prevents shorter keys clobbering longer ones (e.g. `"Empresa"` should not match before `"Empresa *"`).

### Restoring Portuguese

Every modified node's original value is stored in a `Map`:
```javascript
var saved = new Map();
// On translate → saved.set(node, originalValue)
// On restore (PT) → saved.forEach((orig, node) => node.textContent = orig); saved.clear();
```

### Adding new translations

Open `Views/Shared/_Layout.cshtml` and find `var PT_EN = { ... }`. Add:

```javascript
'Texto em português': 'Text in English',
```

Tips:
- Use the **full text as it appears in the DOM** (including emoji prefix if present).
- For dynamically generated text (JS-rendered), place `data-i18n="key"` on the element — the system handles those via `document.querySelectorAll('[data-i18n]')`.
- The two-pass system means both `'💼 Oportunidades': '💼 Opportunities'` AND `'Oportunidades': 'Opportunities'` can coexist; the longer key wins in Pass 2.

---

## Logo Upload Flow

Company logos are uploaded from `Companies/Edit.cshtml`. The flow:

1. User selects an image file in the form (separate from the main edit form).
2. `POST /Companies/UploadLogo?id={companyId}` hits `CompaniesController.UploadLogo`.
3. `IFileStorageService.SaveFileAsync` saves the file to `wwwroot/uploads/companies/{guid}.jpg` (the **MVC app's** wwwroot, so it's always reachable from the browser).
4. The action calls `PUT /api/companies/{id}` with the updated `CompanyLogoUrl` field so the API DB record is updated.
5. Redirect to `Edit` view — the new logo is now served as `/uploads/companies/{guid}.jpg`.

---

## Running this project

```bash
# From APIPSI16_4/
dotnet run --project XcelerateLinks/XcelerateLinks.csproj
# MVC app available at https://localhost:5001
```

Configure `appsettings.json`:
```json
{
  "Api": { "BaseUrl": "https://localhost:7263/" },
  "ConnectionStrings": { "DefaultConnection": "Server=...;..." }
}
```

The API must be running (or the same process includes the API assembly via `ProjectReference`).
