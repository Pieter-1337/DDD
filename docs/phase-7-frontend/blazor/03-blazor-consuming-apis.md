# Blazor — Consuming APIs

This document covers how Blazor Server applications consume backend APIs using typed HttpClient services, Aspire service discovery, and error handling patterns.

---

## How Blazor Server Calls APIs

Blazor Server runs on the server, making HTTP calls server-to-server:

- **No CORS issues** — All API calls originate from the server, not the browser
- **Typed HttpClient pattern** — Uses `IHttpClientFactory` for resilient, configurable HTTP clients
- **Aspire service discovery** — Resolves service names to URLs automatically at runtime
- **Server-side execution** — API calls happen in the SignalR circuit context, not in JavaScript

This architecture means your Blazor app can call internal APIs that aren't exposed to the public internet.

---

## Typed HttpClient Service

The typed HttpClient pattern creates a dedicated service class that encapsulates all API calls for a specific bounded context.

### Implementation

**File**: `C:\projects\DDD\DDD\WebApplications\Scheduling.BlazorApp\Services\PatientApiService.cs`

```csharp
using System.Net.Http.Json;

namespace Scheduling.BlazorApp.Services;

public class PatientApiService
{
    private readonly HttpClient _httpClient;

    public PatientApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<PatientDto>> GetAllPatientsAsync(string? status = null)
    {
        var url = string.IsNullOrEmpty(status)
            ? "/api/patients"
            : $"/api/patients?status={status}";
        return await _httpClient.GetFromJsonAsync<List<PatientDto>>(url) ?? [];
    }

    public async Task<PatientDto?> GetPatientAsync(Guid patientId)
    {
        return await _httpClient.GetFromJsonAsync<PatientDto>($"/api/patients/{patientId}");
    }

    public async Task<CreatePatientResponse> CreatePatientAsync(CreatePatientRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/patients", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreatePatientResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize response");
    }

    public async Task SuspendPatientAsync(Guid patientId)
    {
        var response = await _httpClient.PostAsync($"/api/patients/{patientId}/suspend", null);
        response.EnsureSuccessStatusCode();
    }
}
```

### Benefits of Typed HttpClient

- **Type safety** — IntelliSense and compile-time checking
- **Testability** — Easy to mock for unit tests
- **Encapsulation** — All patient API logic in one place
- **DI-friendly** — Injected into components via constructor
- **Resilience** — IHttpClientFactory manages connection pooling and DNS refresh

---

## DTO Records

DTOs (Data Transfer Objects) define the contract between Blazor app and backend API. Use records for immutability and concise syntax.

### Implementation

**File**: `C:\projects\DDD\DDD\WebApplications\Scheduling.BlazorApp\Services\PatientDto.cs`

```csharp
namespace Scheduling.BlazorApp.Services;

public record PatientDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    DateTime DateOfBirth,
    string Status);

public record CreatePatientRequest(
    string FirstName,
    string LastName,
    string Email,
    DateTime DateOfBirth);

public record CreatePatientResponse(
    bool Success,
    Guid PatientId,
    List<string>? Errors);
```

### DTO Design Guidelines

- **Match backend shape** — Properties must align with API response JSON
- **Use records** — Immutable, value-based equality, concise
- **Separate request/response** — Don't reuse DTOs for different purposes
- **Nullable for optional fields** — Use `?` for fields that may be absent
- **Validation in API** — DTOs are data containers, not domain models

---

## Registration in Program.cs

Register the typed HttpClient with Aspire service discovery in `Program.cs`.

### Configuration

**File**: `C:\projects\DDD\DDD\WebApplications\Scheduling.BlazorApp\Program.cs`

```csharp
// Register typed HttpClient with Aspire service discovery
builder.Services.AddHttpClient<PatientApiService>(client =>
{
    client.BaseAddress = new Uri("https+http://scheduling-webapi");
});
```

### What This Does

1. **Creates HttpClient instance** — IHttpClientFactory manages the lifetime
2. **Injects into PatientApiService** — Constructor receives configured HttpClient
3. **Enables service discovery** — Aspire resolves `scheduling-webapi` to actual URL
4. **Sets base address** — All relative URLs in service resolve against this base

---

## Aspire Service Discovery

Aspire automatically resolves service names to runtime URLs, eliminating hardcoded endpoints.

### How It Works

```csharp
client.BaseAddress = new Uri("https+http://scheduling-webapi");
```

- **Prefix `https+http://`** — Tells Aspire to prefer HTTPS but fall back to HTTP if unavailable
- **Service name `scheduling-webapi`** — Matches the resource name in `AppHost.cs`
- **Runtime resolution** — Aspire injects the actual URL (e.g., `https://localhost:7123`) at startup

### AppHost Configuration

**File**: `C:\projects\DDD\DDD\Aspire.AppHost\Program.cs`

```csharp
var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi");

var blazorApp = builder.AddProject<Projects.Scheduling_BlazorApp>("scheduling-blazorapp")
    .WithReference(schedulingApi); // Enables service discovery
```

The `WithReference()` call:
- Injects service discovery configuration into Blazor app
- Makes `scheduling-webapi` resolvable as a service name
- Handles dynamic port allocation in development

---

## Error Handling Pattern

Robust error handling improves user experience and debugging. Handle HTTP errors gracefully and display actionable messages.

### Service-Level Error Handling

```csharp
public async Task<List<PatientDto>> GetAllPatientsAsync(string? status = null)
{
    try
    {
        var url = string.IsNullOrEmpty(status)
            ? "/api/patients"
            : $"/api/patients?status={status}";
        return await _httpClient.GetFromJsonAsync<List<PatientDto>>(url) ?? [];
    }
    catch (HttpRequestException ex)
    {
        // Log the error (omitted for brevity)
        throw new InvalidOperationException("Failed to retrieve patients from API", ex);
    }
}
```

### Component-Level Error Handling

**File**: `C:\projects\DDD\DDD\WebApplications\Scheduling.BlazorApp\Components\Pages\Patients\PatientList.razor.cs`

```csharp
private string? errorMessage;
private List<PatientDto> patients = [];

protected override async Task OnInitializedAsync()
{
    try
    {
        patients = await PatientApi.GetAllPatientsAsync(selectedStatus);
        errorMessage = null;
    }
    catch (HttpRequestException ex)
    {
        errorMessage = $"Failed to load patients: {ex.Message}";
    }
    catch (InvalidOperationException ex)
    {
        errorMessage = $"An error occurred: {ex.Message}";
    }
}
```

### UI Error Display

**File**: `C:\projects\DDD\DDD\WebApplications\Scheduling.BlazorApp\Components\Pages\Patients\PatientList.razor`

```razor
@if (errorMessage is not null)
{
    <FluentMessageBar Intent="MessageBarIntent.Error" Style="margin-bottom: 1rem;">
        @errorMessage
    </FluentMessageBar>
}

@if (patients.Any())
{
    <FluentDataGrid Items="@patients.AsQueryable()">
        <!-- Grid columns -->
    </FluentDataGrid>
}
else if (errorMessage is null)
{
    <p>No patients found.</p>
}
```

### Error Handling Best Practices

- **Catch specific exceptions** — `HttpRequestException`, `InvalidOperationException`, `JsonException`
- **Display user-friendly messages** — Avoid technical stack traces in UI
- **Log detailed errors** — Use `ILogger` for debugging (server-side logs)
- **Provide context** — Include operation name in error message
- **Handle 404 vs 500** — Differentiate "not found" from "server error"

---

## HttpClient Comparison Table

| Approach | Registration | When to Use | Example |
|----------|-------------|-------------|---------|
| **Typed HttpClient** | `AddHttpClient<TService>()` | Most cases — type safety, testable, DI-friendly | `PatientApiService` with injected `HttpClient` |
| **Named HttpClient** | `AddHttpClient("name")` | Multiple services share similar config | `IHttpClientFactory.CreateClient("api")` |
| **IHttpClientFactory directly** | `AddHttpClient()` | Rare — full control over client creation | Manual client creation in service |

### Recommendation

**Use Typed HttpClient** for all bounded context API services. It provides the best balance of type safety, testability, and maintainability.

---

## Testing API Services

API services can be tested by mocking `HttpClient` or using libraries like `MockHttp`.

### Example Test Stub

```csharp
[TestClass]
public class PatientApiServiceTests
{
    [TestMethod]
    public async Task GetPatientAsync_ValidId_ReturnsPatient()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("https://localhost/api/patients/*")
            .Respond("application/json", "{ \"id\": \"...\", \"firstName\": \"John\" }");

        var client = mockHttp.ToHttpClient();
        client.BaseAddress = new Uri("https://localhost");
        var service = new PatientApiService(client);

        // Act
        var result = await service.GetPatientAsync(Guid.NewGuid());

        // Assert
        result.ShouldNotBeNull();
        result.FirstName.ShouldBe("John");
    }
}
```

Full testing patterns are covered in **Phase 4: Testing** documentation.

---

## Verification Checklist

Use this checklist to verify your API consumption implementation:

- [ ] `PatientApiService` registered as typed HttpClient in `Program.cs`
- [ ] Aspire service discovery configured with `https+http://scheduling-webapi`
- [ ] `GetAllPatientsAsync` works with and without status filter parameter
- [ ] `GetPatientAsync` returns patient by ID or null if not found
- [ ] `CreatePatientAsync` posts data and returns `CreatePatientResponse`
- [ ] `SuspendPatientAsync` calls suspend endpoint and handles success
- [ ] Error handling catches `HttpRequestException` and displays user-friendly messages
- [ ] DTOs match backend API response shapes (run API and inspect JSON)
- [ ] FluentUI `MessageBar` displays errors with appropriate intent
- [ ] No CORS errors in browser console (Blazor Server is server-to-server)

---

## Navigation

- **Previous**: [02-blazor-components-and-routing.md](./02-blazor-components-and-routing.md)
- **Next**: [04-blazor-state-management.md](./04-blazor-state-management.md)
- **Up**: [Phase 7 Frontend Index](../README.md)
