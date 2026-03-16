# Blazor — State Management

State management in Blazor Server determines how data persists and is shared across components during a user's session. Understanding the different scopes and approaches is critical for building maintainable Blazor applications.

---

## State in Blazor Server

### State Scopes

**Component State**
- Local to the component instance
- Stored in component fields
- Lost on navigation or component disposal
- Use for temporary UI state

**Scoped Services**
- Shared across all components within a SignalR circuit
- Tied to the user's session
- Survives navigation within the same circuit
- Lost when the circuit is disposed (connection timeout, user closes tab)

**Singleton Services**
- Shared across all users and circuits
- Lives for the application lifetime
- Use with caution—state is visible to all users
- Appropriate for caching, lookups, configuration

### SignalR Circuit Lifecycle

In Blazor Server, each user session has a unique SignalR circuit. The circuit:
- Maintains server-side state
- Handles UI events and rendering
- Reconnects automatically if the WebSocket drops temporarily
- Is disposed after a timeout (default 3 minutes of inactivity)

When the circuit is disposed, all scoped services and component state are lost.

---

## State Management Approaches

| Approach | Scope | Lifetime | Use Case |
|----------|-------|----------|----------|
| Component fields | Single component | Component lifecycle | Form values, local UI state |
| `[CascadingParameter]` | Component tree | Parent component lifecycle | Theme, user info, context |
| Scoped service | User session (circuit) | SignalR circuit | Cross-page state, notifications |
| Singleton service | Application-wide | Application lifetime | Caching, shared lookups |
| Browser storage | Browser | Persistent | User preferences, tokens |

---

## Component State

The simplest form of state management—store data in component fields.

### Example: Patient List Component

**File**: `C:\projects\DDD\DDD\05. Frontend\Blazor\Scheduling.BlazorApp\Components\Pages\Patients\PatientList.razor`

```razor
@page "/patients"
@inject IPatientApiClient PatientApi

<PageTitle>Patients</PageTitle>

<FluentLabel Typography="Typography.H3">Patients</FluentLabel>

@if (isLoading)
{
    <FluentProgressRing />
}
else if (patients is null || patients.Count == 0)
{
    <FluentMessageBar Intent="MessageBarIntent.Info">
        No patients found.
    </FluentMessageBar>
}
else
{
    <FluentDataGrid Items="@patients.AsQueryable()">
        <PropertyColumn Property="@(p => p.PatientId)" />
        <PropertyColumn Property="@(p => p.FullName)" />
        <PropertyColumn Property="@(p => p.Status)" />
    </FluentDataGrid>
}

@code {
    // Component state—lost when navigating away
    private List<PatientDto>? patients;
    private string? selectedStatus;
    private bool isLoading = true;

    protected override async Task OnInitializedAsync()
    {
        isLoading = true;
        patients = await PatientApi.GetAllPatientsAsync();
        isLoading = false;
    }
}
```

**When to use**:
- Form field values
- Loading indicators
- Filter/sort state for a single page
- Temporary UI state (expanded panels, selected tabs)

**Limitations**:
- State resets on navigation
- Cannot share state with other components
- Cannot persist across page reloads

---

## Scoped Service State

Use scoped services to share state across components within the same user session.

### Use Case: Cross-Page Notifications

After creating a patient, you navigate to the patient list and want to show "Patient created successfully". Component state won't work because the creation component is disposed.

### Implementation: NotificationService

**File**: `C:\projects\DDD\DDD\05. Frontend\Blazor\Scheduling.BlazorApp\Services\NotificationService.cs`

```csharp
namespace Scheduling.BlazorApp.Services;

/// <summary>
/// Scoped service for displaying notifications across page navigations.
/// Notifications persist until the user navigates to a new page or they are explicitly cleared.
/// </summary>
public class NotificationService
{
    private readonly List<Notification> _notifications = [];

    /// <summary>
    /// Gets the current list of notifications.
    /// </summary>
    public IReadOnlyList<Notification> Notifications => _notifications;

    /// <summary>
    /// Event fired when the notification list changes.
    /// </summary>
    public event Action? OnChange;

    /// <summary>
    /// Displays a success message.
    /// </summary>
    public void ShowSuccess(string message)
    {
        _notifications.Add(new Notification(message, NotificationType.Success));
        OnChange?.Invoke();
    }

    /// <summary>
    /// Displays an error message.
    /// </summary>
    public void ShowError(string message)
    {
        _notifications.Add(new Notification(message, NotificationType.Error));
        OnChange?.Invoke();
    }

    /// <summary>
    /// Displays a warning message.
    /// </summary>
    public void ShowWarning(string message)
    {
        _notifications.Add(new Notification(message, NotificationType.Warning));
        OnChange?.Invoke();
    }

    /// <summary>
    /// Displays an informational message.
    /// </summary>
    public void ShowInfo(string message)
    {
        _notifications.Add(new Notification(message, NotificationType.Info));
        OnChange?.Invoke();
    }

    /// <summary>
    /// Clears all notifications.
    /// </summary>
    public void Clear()
    {
        _notifications.Clear();
        OnChange?.Invoke();
    }
}

/// <summary>
/// Represents a user notification.
/// </summary>
/// <param name="Message">The notification message.</param>
/// <param name="Type">The notification type (severity).</param>
public record Notification(string Message, NotificationType Type);

/// <summary>
/// Notification severity levels.
/// </summary>
public enum NotificationType
{
    Success,
    Error,
    Warning,
    Info
}
```

### Register as Scoped

**File**: `C:\projects\DDD\DDD\05. Frontend\Blazor\Scheduling.BlazorApp\Program.cs`

```csharp
// Scoped services—one instance per SignalR circuit (user session)
builder.Services.AddScoped<NotificationService>();
```

---

## Displaying Notifications in Layout

Create a component that subscribes to notification changes and displays them.

### NotificationDisplay Component

**File**: `C:\projects\DDD\DDD\05. Frontend\Blazor\Scheduling.BlazorApp\Components\Layout\NotificationDisplay.razor`

```razor
@inject NotificationService NotificationService
@implements IDisposable

@if (NotificationService.Notifications.Any())
{
    <div class="notification-container">
        @foreach (var notification in NotificationService.Notifications)
        {
            <FluentMessageBar Intent="@GetIntent(notification.Type)" Class="mb-3">
                @notification.Message
            </FluentMessageBar>
        }
    </div>
}

@code {
    protected override void OnInitialized()
    {
        // Subscribe to changes—when notifications are added/cleared, re-render this component
        NotificationService.OnChange += StateHasChanged;
    }

    private MessageBarIntent GetIntent(NotificationType type) => type switch
    {
        NotificationType.Success => MessageBarIntent.Success,
        NotificationType.Error => MessageBarIntent.Error,
        NotificationType.Warning => MessageBarIntent.Warning,
        NotificationType.Info => MessageBarIntent.Info,
        _ => MessageBarIntent.Info
    };

    public void Dispose()
    {
        // Unsubscribe to prevent memory leaks
        NotificationService.OnChange -= StateHasChanged;
    }
}
```

### Add to MainLayout

**File**: `C:\projects\DDD\DDD\05. Frontend\Blazor\Scheduling.BlazorApp\Components\Layout\MainLayout.razor`

```razor
@inherits LayoutComponentBase

<FluentLayout>
    <FluentHeader>
        Scheduling System
    </FluentHeader>
    <FluentStack Orientation="Orientation.Horizontal" Width="100%">
        <NavMenu />
        <FluentBodyContent>
            <div class="content">
                <NotificationDisplay />
                @Body
            </div>
        </FluentBodyContent>
    </FluentStack>
</FluentLayout>
```

---

## Using Notifications After Navigation

### Example: Create Patient Flow

**File**: `C:\projects\DDD\DDD\05. Frontend\Blazor\Scheduling.BlazorApp\Components\Pages\Patients\CreatePatient.razor`

```razor
@page "/patients/create"
@inject IPatientApiClient PatientApi
@inject NotificationService NotificationService
@inject NavigationManager Navigation

<PageTitle>Create Patient</PageTitle>

<FluentLabel Typography="Typography.H3">Create Patient</FluentLabel>

<EditForm Model="@model" OnValidSubmit="HandleSubmitAsync">
    <FluentValidationSummary />

    <FluentTextField @bind-Value="model.FirstName" Label="First Name" Required />
    <FluentTextField @bind-Value="model.LastName" Label="Last Name" Required />
    <FluentTextField @bind-Value="model.DateOfBirth" Label="Date of Birth" Required Type="date" />

    <FluentButton Type="ButtonType.Submit" Appearance="Appearance.Accent" Loading="@isSubmitting">
        Create Patient
    </FluentButton>
</EditForm>

@code {
    private CreatePatientModel model = new();
    private bool isSubmitting;

    private async Task HandleSubmitAsync()
    {
        isSubmitting = true;

        try
        {
            var command = new CreatePatientCommand(
                model.FirstName,
                model.LastName,
                DateOnly.Parse(model.DateOfBirth));

            var response = await PatientApi.CreatePatientAsync(command);

            // Add notification to scoped service—it will survive navigation
            NotificationService.ShowSuccess($"Patient {response.PatientId} created successfully");

            // Navigate to patient list—notification will display there
            Navigation.NavigateTo("/patients");
        }
        catch (ApiException ex)
        {
            NotificationService.ShowError($"Failed to create patient: {ex.Message}");
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private class CreatePatientModel
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty;
    }
}
```

### Clear Notifications on Navigation

Add to `PatientList.razor` to clear notifications after displaying:

```razor
@code {
    protected override async Task OnInitializedAsync()
    {
        isLoading = true;
        patients = await PatientApi.GetAllPatientsAsync();
        isLoading = false;

        // Clear notifications after a delay so user can read them
        await Task.Delay(5000);
        NotificationService.Clear();
    }
}
```

---

## StateHasChanged and Re-rendering

### Automatic Calls

Blazor automatically calls `StateHasChanged()` after:
- Event handlers (`@onclick`, `@onchange`, etc.)
- `OnInitialized`, `OnInitializedAsync`, `OnParametersSet`, `OnParametersSetAsync`
- `OnAfterRender`, `OnAfterRenderAsync`

### Manual Calls Required

Call `StateHasChanged()` manually when state changes outside of Blazor's event loop:

**Service Callbacks**
```csharp
protected override void OnInitialized()
{
    // Subscribe to service event
    NotificationService.OnChange += StateHasChanged;
}
```

**Background Tasks**
```csharp
private async Task StartBackgroundPolling()
{
    while (!cancellationToken.IsCancellationRequested)
    {
        await Task.Delay(5000, cancellationToken);

        // Update component state from background task
        notifications = await GetNotificationsAsync();

        // Must call StateHasChanged to re-render
        await InvokeAsync(StateHasChanged);
    }
}
```

### InvokeAsync for Thread Safety

Always use `InvokeAsync(StateHasChanged)` when updating UI from a background thread or callback outside Blazor's synchronization context.

```csharp
// Safe—called from Blazor event handler
private void OnButtonClick()
{
    counter++;
    // Blazor calls StateHasChanged automatically
}

// Safe—manual call on UI thread
private void OnServiceEvent()
{
    message = "Event received";
    StateHasChanged();
}

// UNSAFE—called from background thread
private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
{
    message = "Timer elapsed";
    StateHasChanged(); // Exception—not on UI thread
}

// Safe—marshaled to UI thread
private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
{
    message = "Timer elapsed";
    InvokeAsync(StateHasChanged); // Marshals to UI thread
}
```

---

## Blazor Server vs WebAssembly State

| Concern | Blazor Server | Blazor WebAssembly |
|---------|---------------|-------------------|
| **State location** | Server memory | Browser memory (WASM runtime) |
| **State lifetime** | SignalR circuit | Browser tab lifetime |
| **Lost when** | Circuit disposed (timeout, tab close) | Tab closed, browser refresh |
| **Cross-tab sharing** | No (each tab = new circuit) | No (each tab = isolated WASM instance) |
| **Persistence** | Server-side session/cache | localStorage, sessionStorage, IndexedDB |
| **Security** | State not accessible to client | State in client memory (visible in DevTools) |
| **Scalability** | More memory per user on server | No server memory—runs in browser |

### Blazor Server Circuit Lifetime

```
User opens app → SignalR circuit created → Scoped services instantiated
  ↓
User navigates between pages → Circuit persists → Scoped services persist
  ↓
User inactive for 3 minutes → Circuit disposed → Scoped services disposed
  OR
User closes tab → Circuit disposed → Scoped services disposed
```

### Persisting State Across Sessions

For state that should survive circuit disposal (user preferences, authentication tokens):

**Use Browser Storage**
- `localStorage`—persists until explicitly cleared
- `sessionStorage`—persists until browser tab closed
- IndexedDB—for larger datasets

**Use Server-Side Session**
- Distributed cache (Redis, SQL Server)
- Database with session key

**Example**: Blazor Server with localStorage via JSInterop
```csharp
// Save to browser storage
await JSRuntime.InvokeVoidAsync("localStorage.setItem", "theme", "dark");

// Load from browser storage
var theme = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "theme");
```

---

## Best Practices

### Component State
- Keep component state minimal
- Use private fields for local UI state
- Initialize in `OnInitialized` or `OnInitializedAsync`
- Reset state in `OnParametersSet` if component is reused

### Scoped Services
- Use for cross-component, within-session state
- Fire `OnChange` event when state changes
- Subscribers must call `StateHasChanged` in event handler
- Implement `IDisposable` in components to unsubscribe

### Singleton Services
- Never store user-specific state
- Use for app-wide configuration, caching
- Ensure thread-safe access (use `lock` or `ConcurrentDictionary`)

### Memory Leaks
- Always unsubscribe from service events in `Dispose`
- Dispose of `IDisposable` resources
- Be cautious with event handlers on long-lived services

```csharp
@implements IDisposable

@code {
    protected override void OnInitialized()
    {
        MyService.OnChange += HandleChange;
    }

    private void HandleChange()
    {
        StateHasChanged();
    }

    public void Dispose()
    {
        MyService.OnChange -= HandleChange;
    }
}
```

---

## Verification Checklist

- [ ] Component state used for local UI values (loading, filters)
- [ ] Scoped services used for cross-page state (notifications, user context)
- [ ] Singleton services used only for app-wide state (caching, config)
- [ ] `NotificationService` registered as scoped in `Program.cs`
- [ ] `NotificationDisplay` component added to `MainLayout`
- [ ] Notifications display after patient creation and navigation
- [ ] Error messages display on API failures
- [ ] Notifications cleared appropriately (timeout or manual)
- [ ] `IDisposable` implemented for components subscribing to service events
- [ ] `StateHasChanged` called correctly for service updates
- [ ] `InvokeAsync(StateHasChanged)` used for background thread updates

---

## Navigation

- **Previous**: [03-blazor-consuming-apis.md](./03-blazor-consuming-apis.md)
- **Next**: [05-blazor-forms-and-validation.md](./05-blazor-forms-and-validation.md)
- **Up**: [README.md](./README.md)
