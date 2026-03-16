# Blazor Components and Routing

This document covers the Blazor component model, routing system, and practical implementations for patient list and detail pages using FluentUI components.

---

## 1. The Blazor Component Model

Blazor components are `.razor` files that combine C# logic and HTML markup in a single file.

### Component Structure

```razor
@page "/patients"                        <!-- Route directive -->
@inject PatientApiService PatientApi     <!-- Dependency injection -->

<!-- HTML markup with Razor syntax -->
<h1>Patients</h1>

@code {
    // C# code block
    private List<PatientDto>? patients;

    protected override async Task OnInitializedAsync()
    {
        patients = await PatientApi.GetAllPatientsAsync();
    }
}
```

### Component Lifecycle

| Lifecycle Method | When Called | Use Case |
|------------------|-------------|----------|
| `OnInitialized` / `OnInitializedAsync` | Once when component first created | Load initial data, set up subscriptions |
| `OnParametersSet` / `OnParametersSetAsync` | When parameters change | Respond to parameter changes, reload data |
| `OnAfterRender` / `OnAfterRenderAsync` | After component renders | JavaScript interop, focus elements |
| `Dispose` | When component removed | Clean up resources, unsubscribe events |

**Async is preferred** - Use `OnInitializedAsync` instead of `OnInitialized` for async operations.

### Component Parameters

Parameters allow parent components to pass data to child components:

```csharp
@code {
    [Parameter] public Guid PatientId { get; set; }
    [Parameter] public string? Title { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
}
```

**Key points:**
- `[Parameter]` attribute required
- Public properties only
- Can be value types, reference types, or `EventCallback`
- Changes trigger `OnParametersSetAsync`

### Data Binding

Blazor supports one-way and two-way data binding:

```razor
<!-- One-way binding (component to UI) -->
<p>@patient.FirstName</p>

<!-- Event handling -->
<button @onclick="HandleClick">Click</button>

<!-- Two-way binding (covered in forms doc) -->
<input @bind="patient.Email" />

@code {
    private PatientDto patient = new();

    private void HandleClick()
    {
        // Handle click event
    }
}
```

---

## 2. Routing in Blazor

Blazor uses client-side routing to navigate between pages without full page reloads.

### Route Directives

Use the `@page` directive to make a component routable:

```razor
@page "/patients"                   <!-- Static route -->
@page "/patients/{PatientId:guid}"  <!-- Route with parameter -->
@page "/appointments/{Id:int}"      <!-- Route with type constraint -->
```

### Route Parameters

Extract parameters from the URL:

```razor
@page "/patients/{PatientId:guid}"

@code {
    [Parameter] public Guid PatientId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // PatientId is automatically populated from route
        var patient = await PatientApi.GetPatientAsync(PatientId);
    }
}
```

**Type constraints:**
- `:bool` - Boolean
- `:datetime` - DateTime
- `:decimal` - Decimal
- `:double` - Double
- `:float` - Float
- `:guid` - Guid
- `:int` - 32-bit integer
- `:long` - 64-bit integer

### Programmatic Navigation

Inject `NavigationManager` for code-based navigation:

```razor
@inject NavigationManager Navigation

<button @onclick="@(() => Navigation.NavigateTo("/patients"))">
    Back to List
</button>

@code {
    private void CreatePatient()
    {
        Navigation.NavigateTo("/patients/create");
    }

    private void ViewPatient(Guid id)
    {
        Navigation.NavigateTo($"/patients/{id}");
    }
}
```

### Navigation Links

Use `NavLink` for links with automatic active state styling:

```razor
<NavLink href="/patients" Match="NavLinkMatch.All">
    Patients
</NavLink>

<NavLink href="/appointments" Match="NavLinkMatch.Prefix">
    Appointments
</NavLink>
```

**Match modes:**
- `NavLinkMatch.All` - Exact match only (e.g., `/patients` matches `/patients` but not `/patients/123`)
- `NavLinkMatch.Prefix` - Prefix match (e.g., `/patients` matches `/patients/123`)

---

## 3. Patient List Page Implementation

Create a page to display all patients with filtering and navigation.

### PatientList.razor

**Location:** `05. Frontend/Blazor/Scheduling.BlazorApp/Components/Pages/Patients/PatientList.razor`

```razor
@page "/patients"
@inject PatientApiService PatientApi
@inject NavigationManager Navigation

<PageTitle>Patients</PageTitle>

<FluentStack Orientation="Orientation.Vertical" VerticalGap="16">
    <FluentLabel Typo="Typography.PageTitle">Patients</FluentLabel>

    <FluentStack Orientation="Orientation.Horizontal" HorizontalGap="8">
        <FluentSelect @bind-Value="selectedStatus" TOption="string">
            <FluentOption Value="">All</FluentOption>
            <FluentOption Value="Active">Active</FluentOption>
            <FluentOption Value="Suspended">Suspended</FluentOption>
        </FluentSelect>
        <FluentButton Appearance="Appearance.Accent" OnClick="LoadPatientsAsync">
            Filter
        </FluentButton>
        <FluentButton OnClick="@(() => Navigation.NavigateTo("/patients/create"))">
            Create Patient
        </FluentButton>
    </FluentStack>

    @if (patients is null)
    {
        <FluentProgressRing />
    }
    else
    {
        <FluentDataGrid Items="@patients" Pagination="@pagination">
            <PropertyColumn Property="@(p => p.FirstName)" Title="First Name" Sortable="true" />
            <PropertyColumn Property="@(p => p.LastName)" Title="Last Name" Sortable="true" />
            <PropertyColumn Property="@(p => p.Email)" Title="Email" />
            <PropertyColumn Property="@(p => p.Status)" Title="Status" />
            <TemplateColumn Title="Actions">
                <FluentButton OnClick="@(() => Navigation.NavigateTo($"/patients/{context.Id}"))">
                    View
                </FluentButton>
            </TemplateColumn>
        </FluentDataGrid>

        <FluentPaginator State="@pagination" />
    }
</FluentStack>

@code {
    private IQueryable<PatientDto>? patients;
    private string? selectedStatus;
    private PaginationState pagination = new() { ItemsPerPage = 10 };

    protected override async Task OnInitializedAsync()
    {
        await LoadPatientsAsync();
    }

    private async Task LoadPatientsAsync()
    {
        var result = await PatientApi.GetAllPatientsAsync(selectedStatus);
        patients = result.AsQueryable();
    }
}
```

### Key Components Used

| Component | Purpose |
|-----------|---------|
| `FluentStack` | Flexible layout container with orientation and gap control |
| `FluentLabel` | Text labels with typography variants |
| `FluentSelect` | Dropdown selection |
| `FluentButton` | Buttons with appearance variants |
| `FluentDataGrid` | High-performance data grid with sorting and pagination |
| `FluentProgressRing` | Loading indicator |
| `FluentPaginator` | Pagination controls |

### Loading States

Always show loading indicators while data is being fetched:

```razor
@if (patients is null)
{
    <FluentProgressRing />
}
else
{
    <!-- Render data -->
}
```

**Pattern:**
- Initialize data as `null`
- Check for `null` to show loading state
- Render content once data is loaded

---

## 4. Patient Detail Page Implementation

Create a page to display patient details and allow actions.

### PatientDetail.razor

**Location:** `05. Frontend/Blazor/Scheduling.BlazorApp/Components/Pages/Patients/PatientDetail.razor`

```razor
@page "/patients/{PatientId:guid}"
@inject PatientApiService PatientApi
@inject NavigationManager Navigation

<PageTitle>Patient Details</PageTitle>

@if (patient is null)
{
    <FluentProgressRing />
}
else
{
    <FluentStack Orientation="Orientation.Vertical" VerticalGap="16">
        <FluentStack Orientation="Orientation.Horizontal" HorizontalGap="8">
            <FluentButton IconStart="@(new Icons.Regular.Size20.ArrowLeft())"
                          OnClick="@(() => Navigation.NavigateTo("/patients"))">
                Back
            </FluentButton>
            <FluentLabel Typo="Typography.PageTitle">
                @patient.FirstName @patient.LastName
            </FluentLabel>
        </FluentStack>

        <FluentCard>
            <FluentGrid Spacing="3">
                <FluentGridItem xs="12" sm="6">
                    <strong>Email:</strong> @patient.Email
                </FluentGridItem>
                <FluentGridItem xs="12" sm="6">
                    <strong>Status:</strong>
                    <FluentBadge Appearance="@GetStatusAppearance(patient.Status)">
                        @patient.Status
                    </FluentBadge>
                </FluentGridItem>
                <FluentGridItem xs="12" sm="6">
                    <strong>Date of Birth:</strong> @patient.DateOfBirth.ToShortDateString()
                </FluentGridItem>
                <FluentGridItem xs="12" sm="6">
                    <strong>Phone Number:</strong> @(patient.PhoneNumber ?? "N/A")
                </FluentGridItem>
            </FluentGrid>
        </FluentCard>

        @if (patient.Status != "Suspended")
        {
            <FluentButton Appearance="Appearance.Accent"
                          IconStart="@(new Icons.Regular.Size20.PersonProhibited())"
                          OnClick="SuspendPatientAsync">
                Suspend Patient
            </FluentButton>
        }
    </FluentStack>
}

@code {
    [Parameter] public Guid PatientId { get; set; }
    private PatientDto? patient;

    protected override async Task OnInitializedAsync()
    {
        patient = await PatientApi.GetPatientAsync(PatientId);
    }

    private async Task SuspendPatientAsync()
    {
        await PatientApi.SuspendPatientAsync(PatientId);
        patient = await PatientApi.GetPatientAsync(PatientId);
    }

    private Appearance GetStatusAppearance(string status)
    {
        return status switch
        {
            "Active" => Appearance.Success,
            "Suspended" => Appearance.Neutral,
            _ => Appearance.Lightweight
        };
    }
}
```

### FluentUI Grid Layout

`FluentGrid` uses a 12-column responsive grid system:

```razor
<FluentGrid Spacing="3">
    <FluentGridItem xs="12" sm="6" md="4">
        <!-- Takes full width on mobile, half on tablet, third on desktop -->
    </FluentGridItem>
</FluentGrid>
```

**Breakpoints:**
- `xs` - Extra small (mobile)
- `sm` - Small (tablet)
- `md` - Medium (desktop)
- `lg` - Large
- `xl` - Extra large

---

## 5. FluentUI DataGrid Deep Dive

The `FluentDataGrid` component provides high-performance data display with built-in sorting and pagination.

### PropertyColumn

For simple property binding:

```razor
<FluentDataGrid Items="@patients">
    <PropertyColumn Property="@(p => p.FirstName)" Title="First Name" Sortable="true" />
    <PropertyColumn Property="@(p => p.Email)" Title="Email" />
</FluentDataGrid>
```

**Key attributes:**
- `Property` - Lambda expression selecting the property
- `Title` - Column header text
- `Sortable` - Enable column sorting
- `IsDefaultSortColumn` - Default sort column
- `InitialSortDirection` - `SortDirection.Ascending` or `Descending`

### TemplateColumn

For custom rendering:

```razor
<TemplateColumn Title="Actions">
    <FluentButton OnClick="@(() => HandleClick(context.Id))">
        View
    </FluentButton>
</TemplateColumn>

<TemplateColumn Title="Status">
    <FluentBadge Appearance="@(context.IsActive ? Appearance.Success : Appearance.Neutral)">
        @context.Status
    </FluentBadge>
</TemplateColumn>
```

**`context` variable:**
- Refers to the current row item
- Type matches the grid's `Items` type

### Pagination

Add pagination with `PaginationState`:

```razor
<FluentDataGrid Items="@patients" Pagination="@pagination">
    <!-- Columns -->
</FluentDataGrid>

<FluentPaginator State="@pagination" />

@code {
    private PaginationState pagination = new() { ItemsPerPage = 10 };
}
```

### Complete Example

```razor
<FluentDataGrid Items="@patients" Pagination="@pagination">
    <PropertyColumn Property="@(p => p.FirstName)"
                    Title="First Name"
                    Sortable="true"
                    IsDefaultSortColumn="true" />
    <PropertyColumn Property="@(p => p.LastName)"
                    Title="Last Name"
                    Sortable="true" />
    <PropertyColumn Property="@(p => p.Email)" Title="Email" />
    <TemplateColumn Title="Status">
        <FluentBadge Appearance="@(context.Status == "Active" ? Appearance.Success : Appearance.Neutral)">
            @context.Status
        </FluentBadge>
    </TemplateColumn>
    <TemplateColumn Title="Actions">
        <FluentButton OnClick="@(() => Navigation.NavigateTo($"/patients/{context.Id}"))">
            View
        </FluentButton>
    </TemplateColumn>
</FluentDataGrid>

<FluentPaginator State="@pagination" />
```

---

## 6. Updating the Navigation Menu

Add patient links to the main navigation menu.

### NavMenu.razor

**Location:** `05. Frontend/Blazor/Scheduling.BlazorApp/Components/Layout/NavMenu.razor`

```razor
@inject NavigationManager Navigation

<FluentNavMenu Title="Scheduling System" Width="250">
    <FluentNavLink Href="/" Icon="@(new Icons.Regular.Size20.Home())">
        Home
    </FluentNavLink>

    <FluentNavLink Href="/patients" Icon="@(new Icons.Regular.Size20.People())">
        Patients
    </FluentNavLink>

    <FluentNavLink Href="/appointments" Icon="@(new Icons.Regular.Size20.Calendar())">
        Appointments
    </FluentNavLink>
</FluentNavMenu>
```

### FluentUI Icons

FluentUI provides a comprehensive icon library:

```razor
@using Microsoft.FluentUI.AspNetCore.Components

<!-- Regular icons -->
<FluentIcon Value="@(new Icons.Regular.Size20.Home())" />
<FluentIcon Value="@(new Icons.Regular.Size20.People())" />
<FluentIcon Value="@(new Icons.Regular.Size20.Calendar())" />

<!-- Filled icons -->
<FluentIcon Value="@(new Icons.Filled.Size20.Home())" />

<!-- Different sizes -->
<FluentIcon Value="@(new Icons.Regular.Size16.Home())" />
<FluentIcon Value="@(new Icons.Regular.Size24.Home())" />
```

**Icon sizes available:** 12, 16, 20, 24, 28, 32, 48

---

## 7. Component Communication Patterns

### Parent to Child (Parameters)

Parent passes data to child via parameters:

```razor
<!-- Parent.razor -->
<ChildComponent PatientId="@selectedPatientId" Title="Patient Details" />

<!-- ChildComponent.razor -->
@code {
    [Parameter] public Guid PatientId { get; set; }
    [Parameter] public string? Title { get; set; }
}
```

### Child to Parent (EventCallback)

Child notifies parent via event callbacks:

```razor
<!-- ChildComponent.razor -->
<FluentButton OnClick="HandleSave">Save</FluentButton>

@code {
    [Parameter] public EventCallback OnSaved { get; set; }

    private async Task HandleSave()
    {
        // Perform save logic
        await OnSaved.InvokeAsync();
    }
}

<!-- Parent.razor -->
<ChildComponent OnSaved="HandlePatientSaved" />

@code {
    private void HandlePatientSaved()
    {
        // Refresh list, show notification, etc.
    }
}
```

### EventCallback with Arguments

Pass data back to parent:

```razor
<!-- ChildComponent.razor -->
@code {
    [Parameter] public EventCallback<Guid> OnPatientCreated { get; set; }

    private async Task CreatePatient()
    {
        var newId = await CreatePatientAsync();
        await OnPatientCreated.InvokeAsync(newId);
    }
}

<!-- Parent.razor -->
<ChildComponent OnPatientCreated="HandlePatientCreated" />

@code {
    private void HandlePatientCreated(Guid patientId)
    {
        Navigation.NavigateTo($"/patients/{patientId}");
    }
}
```

### Sibling Communication (Shared Service)

Siblings communicate via shared state service (covered in state management doc):

```csharp
// AppState.cs
public class AppState
{
    public event Action? OnChange;

    private Guid? selectedPatientId;

    public void SelectPatient(Guid patientId)
    {
        selectedPatientId = patientId;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}

// Component1.razor
@inject AppState AppState
<FluentButton OnClick="@(() => AppState.SelectPatient(patientId))">
    Select
</FluentButton>

// Component2.razor
@inject AppState AppState
@implements IDisposable

protected override void OnInitialized()
{
    AppState.OnChange += StateHasChanged;
}

public void Dispose()
{
    AppState.OnChange -= StateHasChanged;
}
```

---

## 8. Folder Structure After This Step

```
05. Frontend/Blazor/Scheduling.BlazorApp/
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor
│   │   └── NavMenu.razor                    <- Updated with patient links
│   └── Pages/
│       ├── Home.razor
│       └── Patients/
│           ├── PatientList.razor            <- NEW: Patient list page
│           └── PatientDetail.razor          <- NEW: Patient detail page
├── Services/
│   └── PatientApiService.cs                 <- API client (from previous doc)
└── wwwroot/
    └── app.css
```

---

## 9. Verification Checklist

After implementing these components, verify:

- [ ] Patient List page shows data from API with `FluentDataGrid`
- [ ] Status filter dropdown works (All, Active, Suspended)
- [ ] Filter button reloads data based on selected status
- [ ] Pagination works with `FluentPaginator`
- [ ] Columns are sortable (First Name, Last Name)
- [ ] "Create Patient" button navigates to `/patients/create` (implement in next doc)
- [ ] "View" button navigates to patient detail page
- [ ] Patient Detail page loads patient by ID from route parameter
- [ ] Patient details display in responsive grid layout
- [ ] Status badge shows with correct appearance (Active = green, Suspended = gray)
- [ ] "Suspend" button only shows for non-suspended patients
- [ ] Suspend button works and updates UI
- [ ] "Back" button navigates to patient list
- [ ] Navigation menu shows patient link with icon
- [ ] Navigation menu highlights active page
- [ ] Loading states show `FluentProgressRing` while data loads
- [ ] All navigation is client-side (no full page reloads)

---

## 10. Common Issues and Solutions

### DataGrid Not Showing Data

**Problem:** Grid appears empty even though data is loaded.

**Solution:** Ensure `Items` is `IQueryable<T>`:

```csharp
// GOOD
private IQueryable<PatientDto>? patients;
patients = result.AsQueryable();

// BAD
private List<PatientDto>? patients;
patients = result;  // DataGrid expects IQueryable
```

### Route Parameter Not Populating

**Problem:** `PatientId` is always `Guid.Empty`.

**Solution:** Ensure parameter name matches route exactly (case-sensitive):

```razor
@page "/patients/{PatientId:guid}"

@code {
    [Parameter] public Guid PatientId { get; set; }  // Must match case
}
```

### Loading Indicator Doesn't Show

**Problem:** Page appears blank while loading.

**Solution:** Initialize data as `null`, not empty collection:

```csharp
// GOOD
private IQueryable<PatientDto>? patients;  // null = loading

// BAD
private IQueryable<PatientDto>? patients = Enumerable.Empty<PatientDto>().AsQueryable();  // Not null = loaded
```

---

## Summary

You've implemented the core Blazor component and routing patterns:

1. **Component Model** - Lifecycle methods, parameters, data binding
2. **Routing** - Route directives, parameters, navigation
3. **Patient List Page** - DataGrid with filtering, sorting, pagination
4. **Patient Detail Page** - Responsive layout, conditional rendering
5. **Navigation Menu** - Updated with patient links and icons
6. **Component Communication** - Parameters, event callbacks, shared services

### Key Patterns Used

| Pattern | Use Case |
|---------|----------|
| `OnInitializedAsync` | Load data when component first mounts |
| `[Parameter]` | Accept data from parent or route |
| `NavigationManager` | Programmatic navigation |
| `FluentDataGrid` | Display tabular data with sorting/pagination |
| `FluentProgressRing` | Show loading states |
| Loading pattern | Initialize as `null`, check for `null` to show spinner |

### What's Next

In the next document, we'll build the API client service layer, handle errors, and implement form submission for creating patients.

---

> **Previous:** [01-blazor-project-setup.md](./01-blazor-project-setup.md) - Setting up Blazor Server project with FluentUI
>
> **Next:** [03-blazor-consuming-apis.md](./03-blazor-consuming-apis.md) - Consuming APIs with HttpClient, error handling, and form submission
