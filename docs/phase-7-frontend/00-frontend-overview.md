# Frontend Overview — Patient Management UI

## What We're Building

This phase adds user interfaces to the DDD learning project. Both the **Blazor** and **Angular** tracks build the same patient management UI with identical functionality. Choose either track — or build both to compare frameworks and to enable the BFF pattern exploration in Phase 8.

The UI provides:
- List all patients with optional status filtering
- Create new patients with validation
- View patient details including billing profile
- Suspend and activate patients

### Pages and Endpoints

| Page | API Endpoint | Description |
|------|-------------|-------------|
| **Patient List** | `GET /api/patients?status=` | List all patients, optional status filter (Active, Suspended) |
| **Create Patient** | `POST /api/patients` | Form to create new patient with validation |
| **Patient Detail** | `GET /api/patients/{id}` | View patient details + billing profile |
| **Suspend Patient** | `POST /api/patients/{id}/suspend` | Action button on detail page |
| **Activate Patient** | `POST /api/patients/{id}/activate` | Action button on detail page |
| **Billing Profile** | `GET /api/billingprofiles/{patientId}` (**prerequisite**) | Shown on patient detail page |

---

## Architecture Diagram

```
Browser
   |
   v
+------------------+
|  Frontend App    |
|                  |
|  - Blazor Server |  <-- C# / .NET track
|    OR            |
|  - Angular SPA   |  <-- TypeScript track
+------------------+
   |
   | HTTP requests
   v
+------------------+
|   Backend APIs   |
|                  |
|  - Scheduling    |  <-- Patients, appointments
|  - Billing       |  <-- Billing profiles
+------------------+
   |
   | Integration Events (RabbitMQ)
   v
+------------------+
|   Databases      |
|                  |
|  - Scheduling DB |
|  - Billing DB    |
+------------------+
```

**Notes:**
- Frontends consume backend APIs directly (no BFF for this initial implementation)
- Backend APIs are orchestrated via .NET Aspire locally
- Integration events flow directly between backend APIs (frontends are not involved)

---

## Available API Endpoints

### Scheduling API (`Scheduling.WebApi`)

| Method | Endpoint | Request Body | Response | Description |
|--------|----------|--------------|----------|-------------|
| `GET` | `/api/patients` | Query: `?status=Active` (optional) | `PatientDto[]` | Get all patients, optionally filtered by status |
| `GET` | `/api/patients/{patientId}` | - | `PatientDto` | Get patient by ID |
| `POST` | `/api/patients` | `CreatePatientRequest` | `CreatePatientCommandResponse` | Create new patient |
| `POST` | `/api/patients/{patientId}/suspend` | - | `bool` | Suspend patient |
| `POST` | `/api/patients/{patientId}/activate` | - | `bool` | Activate patient |

### Billing API (`Billing.WebApi`)

| Method | Endpoint | Request Body | Response | Description |
|--------|----------|--------------|----------|-------------|
| `GET` | `/api/billingprofiles/{patientId}` | - | `BillingProfileDto` | **PREREQUISITE:** Get billing profile by patient ID (needs implementation) |

> **Important:** The Billing API currently only creates billing profiles via integration events (`PatientCreatedIntegrationEvent`). A `GET` endpoint with query + handler + controller action must be added before the frontend can display billing profile data.

---

## Request and Response Shapes

### Create Patient Request

```json
POST /api/patients
Content-Type: application/json

{
  "patient": {
    "firstName": "John",
    "lastName": "Doe",
    "email": "john.doe@example.com",
    "dateOfBirth": "1990-05-15T00:00:00Z",
    "phoneNumber": "555-1234",
    "status": "Active"
  }
}
```

**Validation rules:**
- `firstName`: Required
- `lastName`: Required
- `email`: Required, valid email format
- `dateOfBirth`: Required
- `phoneNumber`: Optional
- `status`: Must be valid `PatientStatus` SmartEnum value (`Active` or `Suspended`)

### Create Patient Response

```json
{
  "success": true,
  "patientId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "errors": []
}
```

### Get Patient Response

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "firstName": "John",
  "lastName": "Doe",
  "email": "john.doe@example.com",
  "dateOfBirth": "1990-05-15T00:00:00Z",
  "phoneNumber": "555-1234",
  "status": {
    "name": "Active",
    "value": 1
  }
}
```

**Note:** `PatientStatus` is returned as a SmartEnum object with `name` (string) and `value` (int).

### Get All Patients Response

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "firstName": "John",
    "lastName": "Doe",
    "email": "john.doe@example.com",
    "dateOfBirth": "1990-05-15T00:00:00Z",
    "phoneNumber": "555-1234",
    "status": {
      "name": "Active",
      "value": 1
    }
  },
  {
    "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "firstName": "Jane",
    "lastName": "Smith",
    "email": "jane.smith@example.com",
    "dateOfBirth": "1985-08-22T00:00:00Z",
    "phoneNumber": null,
    "status": {
      "name": "Suspended",
      "value": 2
    }
  }
]
```

---

## Prerequisite: Billing GET Endpoint

The Billing bounded context currently only creates billing profiles reactively via integration events. To display billing profile data on the patient detail page, you must add a query endpoint:

### Required Implementation

**1. Query:**
```csharp
// Billing.Application/BillingProfiles/Queries/GetBillingProfileByPatientIdQuery.cs
public record GetBillingProfileByPatientIdQuery(Guid PatientId) : Query<BillingProfileDto>;
```

**2. Handler:**
```csharp
public class GetBillingProfileByPatientIdQueryHandler
    : IRequestHandler<GetBillingProfileByPatientIdQuery, BillingProfileDto>
{
    private readonly IUnitOfWork _uow;

    public async Task<BillingProfileDto> Handle(
        GetBillingProfileByPatientIdQuery query,
        CancellationToken ct)
    {
        var profile = await _uow.RepositoryFor<BillingProfile>()
            .Query()
            .Where(bp => bp.PatientId == query.PatientId)
            .Select(BillingProfileDto.Project)
            .FirstOrDefaultAsync(ct);

        return profile ?? throw new NotFoundException($"Billing profile for patient {query.PatientId} not found");
    }
}
```

**3. Controller Action:**
```csharp
// Billing.WebApi/Controllers/BillingProfilesController.cs
[HttpGet("{patientId}")]
[ProducesResponseType<BillingProfileDto>(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> GetBillingProfileAsync(Guid patientId)
{
    var response = await _mediator.Send(new GetBillingProfileByPatientIdQuery(patientId));
    return Ok(response);
}
```

Without this endpoint, the frontend can display patient data but not billing profile data.

---

## Track Comparison Table

Both tracks build identical functionality. Choose one or both.

| # | Topic | Blazor Doc | Angular Doc |
|---|-------|-----------|-------------|
| **01** | Project Setup | [blazor/01-blazor-project-setup.md](./blazor/01-blazor-project-setup.md) | [angular/01-angular-project-setup.md](./angular/01-angular-project-setup.md) |
| **02** | Components & Routing | [blazor/02-blazor-components-and-routing.md](./blazor/02-blazor-components-and-routing.md) | [angular/02-angular-components-and-routing.md](./angular/02-angular-components-and-routing.md) |
| **03** | Consuming APIs | [blazor/03-blazor-consuming-apis.md](./blazor/03-blazor-consuming-apis.md) | [angular/03-angular-consuming-apis.md](./angular/03-angular-consuming-apis.md) |
| **04** | State Management | [blazor/04-blazor-state-management.md](./blazor/04-blazor-state-management.md) | [angular/04-angular-state-management.md](./angular/04-angular-state-management.md) |
| **05** | Forms & Validation | [blazor/05-blazor-forms-and-validation.md](./blazor/05-blazor-forms-and-validation.md) | [angular/05-angular-forms-and-validation.md](./angular/05-angular-forms-and-validation.md) |

---

## Technology Comparison

### Blazor Server vs Angular

| Aspect | Blazor Server | Angular |
|--------|--------------|---------|
| **Language** | C# | TypeScript |
| **Rendering** | Server-side via SignalR | Client-side (SPA) |
| **State Management** | Scoped services, component state | Services, signals |
| **Component Library** | FluentUI Blazor (Microsoft Fluent 2 design) | Angular Material |
| **Forms** | `EditForm` + FluentValidation | Reactive Forms + validation |
| **API Client** | Typed `HttpClient` with Aspire service discovery | Angular `HttpClient` + CORS |
| **Bundle Size** | N/A (server-rendered) | ~200KB gzipped (initial load) |
| **Deployment** | ASP.NET Core app | Static files + nginx/CDN |
| **SEO** | Excellent (server-rendered HTML) | Requires SSR or prerendering |
| **Offline Support** | Limited (requires SignalR connection) | Excellent with Service Workers |
| **Learning Curve** | Low (if you know C#) | Medium (TypeScript + RxJS + Signals) |

### When to Choose Blazor

- You're a .NET developer comfortable with C#
- Server-side rendering is preferred (SEO, low client load)
- Real-time updates via SignalR are useful
- You want to share C# models and validation with backend projects
- Team expertise is in .NET

### When to Choose Angular

- You need offline-first capabilities
- Client-side rendering is preferred (lower server load)
- Team expertise is in TypeScript/JavaScript
- You're building a public-facing SPA
- You need a mature SPA ecosystem (RxJS, signals, standalone components)
- Note: TypeScript models must be defined separately (or generated from API specs using tools like NSwag or openapi-generator)

---

## Relationship to BFF Pattern (Phase 8)

Phase 8 documents the **Backend for Frontend (BFF)** pattern as an optional enhancement. BFFs are useful when:
- Multiple frontend applications with different data needs
- Frontend teams own their backend aggregation logic
- Different authentication strategies per frontend

### For This Phase

**Recommendation:** Consume backend APIs directly (no BFF).

```
Browser
   |
   v
Frontend (Blazor or Angular)
   |
   v
Backend APIs (Scheduling, Billing)  <-- Direct consumption
```

> **Note:** Blazor Server is inherently a BFF — it runs server-side, makes API calls server-to-server, and communicates with the browser via SignalR. It can aggregate data from multiple backend APIs without needing a separate BFF service. The BFF pattern in Phase 8 is primarily relevant for the Angular SPA track.

**When to add a BFF:**
- You build both Blazor and Angular frontends with different needs
- You need custom aggregation endpoints per frontend
- You need different authentication strategies

See [phase-8-api-gateway-bff/02-bff-pattern.md](../phase-8-api-gateway-bff/02-bff-pattern.md) for BFF implementation details.

---

## Integration Events and Frontends

**Important:** Frontends do NOT participate in integration events.

```
User creates patient in UI
        |
        v
    POST /api/patients
        |
        v
Scheduling.WebApi handles command
        |
        +-- Saves patient to DB
        |
        +-- Publishes PatientCreatedIntegrationEvent
                |
                v
            RabbitMQ
                |
                v
        Billing.WebApi consumes event
                |
                v
        Creates billing profile
```

**Frontend responsibilities:**
- Submit commands to backend APIs (`POST /api/patients`)
- Display data from queries (`GET /api/patients`)
- Handle validation errors from backend

**Frontend does NOT:**
- Consume integration events directly
- React to domain state changes via RabbitMQ
- Publish integration events

**Why?**
- Integration events are domain concerns (bounded context communication)
- Frontends are UI concerns (user interaction and display)
- Real-time UI updates (if needed) would use SignalR, not RabbitMQ

---

## Verification Checklist

Before starting frontend development, verify:

### Backend APIs Running

- [ ] .NET Aspire AppHost running (`Aspire.AppHost`)
- [ ] Scheduling.WebApi available at Aspire-assigned URL (e.g., `http://localhost:5001`)
- [ ] Billing.WebApi available at Aspire-assigned URL (e.g., `http://localhost:5002`)
- [ ] RabbitMQ running and accessible via Aspire dashboard

### API Endpoints Available

- [ ] `GET /api/patients` returns patient list
- [ ] `GET /api/patients/{id}` returns patient details
- [ ] `POST /api/patients` creates patient
- [ ] `POST /api/patients/{id}/suspend` suspends patient
- [ ] `POST /api/patients/{id}/activate` activates patient
- [ ] **PREREQUISITE:** `GET /api/billingprofiles/{patientId}` implemented (or plan to skip billing profile display)

### Aspire Dashboard

- [ ] Open Aspire dashboard (typically `http://localhost:15000`)
- [ ] Verify all services show healthy status
- [ ] Check logs for Scheduling.WebApi and Billing.WebApi
- [ ] Verify RabbitMQ connection is active

### Test Data

- [ ] At least one patient exists in database (create via Scalar/Swagger at `http://localhost:5001/scalar/v1`)
- [ ] Integration events flow correctly (patient creation triggers billing profile creation)
- [ ] Patient status can be toggled between Active and Suspended

### Development Tools

- [ ] Scalar available for Scheduling API (`http://localhost:5001/scalar/v1`)
- [ ] Scalar available for Billing API (`http://localhost:5002/scalar/v1`)
- [ ] SQL Server accessible (check connection string in user secrets)
- [ ] RabbitMQ management UI accessible via Aspire dashboard

---

## Next Steps

Choose your frontend track:

### Blazor Server

Start with [blazor/01-blazor-project-setup.md](./blazor/01-blazor-project-setup.md) to:
1. Create Blazor Server project
2. Add FluentUI Blazor component library
3. Configure Aspire integration
4. Set up typed HttpClient for API calls

### Angular SPA

Start with [angular/01-angular-project-setup.md](./angular/01-angular-project-setup.md) to:
1. Create Angular project with standalone components
2. Add Angular Material UI library
3. Configure CORS for cross-origin API access
4. Set up HttpClient and environment configuration

### Why build both?

Building both frontends against the same backend APIs unlocks learning opportunities in Phase 8 (BFF pattern). Two frontends with different rendering models (server-side vs client-side SPA) are the natural trigger for introducing separate Backend for Frontend services, each tailored to their frontend's needs.

---

## Summary

### What You'll Learn

- **Blazor Track:** Server-side rendering, SignalR, C# component model, FluentUI Blazor
- **Angular Track:** Client-side SPA, TypeScript, reactive programming, Angular Material

### Key Concepts

1. **Separation of Concerns** - Frontend is UI, backend is domain logic
2. **API-Driven** - Frontends consume REST APIs, no direct database access
3. **Validation** - Backend validates commands via FluentValidation, frontend displays errors
4. **State Management** - Each framework has its own patterns (services, signals, scoped state)
5. **Integration Events** - Flow directly between backend APIs, frontends are not involved

### Best Practices

- **Backend validates, frontend guides** - Server-side validation is authoritative
- **Display-specific models** - Transform backend DTOs into UI-specific view models if needed
- **Handle errors gracefully** - Show user-friendly messages for validation errors and exceptions
- **Avoid business logic in UI** - Frontend orchestrates, domain layer decides
- **Use framework conventions** - Follow Blazor or Angular best practices for components, routing, and state

---

> **Next:** Choose your track:
> - **Blazor:** [blazor/01-blazor-project-setup.md](./blazor/01-blazor-project-setup.md)
> - **Angular:** [angular/01-angular-project-setup.md](./angular/01-angular-project-setup.md)
