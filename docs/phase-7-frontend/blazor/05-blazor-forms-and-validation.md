# Blazor — Forms and Validation

This document covers form handling and validation in Blazor Server applications, demonstrating how to create data entry forms with client-side validation using FluentValidation. We maintain consistency with backend validation patterns while providing immediate user feedback.

---

## Blazor Forms Overview

Blazor provides a comprehensive form handling system through the `EditForm` component and `EditContext` API.

### Core Concepts

**EditForm Component**
The `EditForm` component provides form handling capabilities including validation, submission, and state management.

**EditContext**
The `EditContext` manages form state, tracks field modifications, and coordinates validation. It is automatically created by `EditForm` but can also be explicitly instantiated for advanced scenarios.

**Validation Approaches**

| Approach | Description | Use Case |
|----------|-------------|----------|
| DataAnnotations | Attribute-based validation on model properties | Simple validation scenarios |
| FluentValidation | Code-based validation with fluent API | Complex validation, consistency with backend |

**Why FluentValidation?**
We use FluentValidation in this project for consistency with the backend CQRS pipeline. It provides:
- Separation of validation logic from models
- Complex validation rules
- Reusable and testable validators
- Consistent validation across client and server

---

## Create Patient Form

The Create Patient form demonstrates a complete form implementation with FluentValidation, error handling, and FluentUI components.

### Component Implementation

**File**: `C:\projects\DDD\DDD\Frontend\Blazor\Scheduling.BlazorApp\Components\Pages\Patients\CreatePatient.razor`

```razor
@page "/patients/create"
@inject PatientApiService PatientApi
@inject NavigationManager Navigation
@inject NotificationService NotificationService

<PageTitle>Create Patient</PageTitle>

<FluentLabel Typo="Typography.PageTitle">Create Patient</FluentLabel>

<EditForm Model="@model" OnValidSubmit="HandleValidSubmit" FormName="CreatePatient">
    <FluentValidationValidator />

    <FluentStack Orientation="Orientation.Vertical" VerticalGap="16">
        <div>
            <FluentTextField @bind-Value="model.FirstName" Label="First Name" Required="true" />
            <FluentValidationMessage For="@(() => model.FirstName)" />
        </div>

        <div>
            <FluentTextField @bind-Value="model.LastName" Label="Last Name" Required="true" />
            <FluentValidationMessage For="@(() => model.LastName)" />
        </div>

        <div>
            <FluentTextField @bind-Value="model.Email" Label="Email" InputType="InputType.Email" Required="true" />
            <FluentValidationMessage For="@(() => model.Email)" />
        </div>

        <div>
            <FluentDatePicker @bind-Value="model.DateOfBirth" Label="Date of Birth" />
            <FluentValidationMessage For="@(() => model.DateOfBirth)" />
        </div>

        <FluentStack Orientation="Orientation.Horizontal" HorizontalGap="8">
            <FluentButton Type="ButtonType.Submit" Appearance="Appearance.Accent"
                          Disabled="@isSubmitting">
                @(isSubmitting ? "Creating..." : "Create Patient")
            </FluentButton>
            <FluentButton OnClick="@(() => Navigation.NavigateTo("/patients"))">Cancel</FluentButton>
        </FluentStack>

        @if (serverErrors?.Any() == true)
        {
            <FluentMessageBar Intent="MessageBarIntent.Error">
                <ul>
                    @foreach (var error in serverErrors)
                    {
                        <li>@error</li>
                    }
                </ul>
            </FluentMessageBar>
        }
    </FluentStack>
</EditForm>

@code {
    private CreatePatientModel model = new();
    private bool isSubmitting;
    private List<string>? serverErrors;

    private async Task HandleValidSubmit()
    {
        isSubmitting = true;
        serverErrors = null;

        try
        {
            var request = new CreatePatientRequest(
                model.FirstName!,
                model.LastName!,
                model.Email!,
                model.DateOfBirth!.Value);

            var response = await PatientApi.CreatePatientAsync(request);

            if (response.Success)
            {
                NotificationService.ShowSuccess("Patient created successfully");
                Navigation.NavigateTo("/patients");
            }
            else
            {
                serverErrors = response.Errors ?? ["An unknown error occurred"];
            }
        }
        catch (HttpRequestException ex)
        {
            serverErrors = [$"Failed to create patient: {ex.Message}"];
        }
        finally
        {
            isSubmitting = false;
        }
    }
}
```

### Key Features

**Form Binding**
The `Model` parameter binds the form to the `CreatePatientModel` instance. All field changes are tracked by `EditContext`.

**Validation Integration**
The `<FluentValidationValidator />` component integrates FluentValidation with Blazor's validation system. It discovers validators registered in dependency injection.

**Validation Messages**
The `<FluentValidationMessage For="..." />` component displays validation errors for specific fields. It automatically shows/hides based on validation state.

**Submit Handling**
The `OnValidSubmit` event fires only when client-side validation passes. The `HandleValidSubmit` method handles the API call and error display.

**Submit Button State**
The `isSubmitting` flag prevents double-submission by disabling the button and showing a "Creating..." message during API calls.

**Server Error Display**
The `serverErrors` list captures validation errors returned from the API and displays them in a `FluentMessageBar`.

---

## Form Model with FluentValidation

The form model is a simple POCO with nullable properties. FluentValidation handles all validation logic separately.

### Form Model

**File**: `C:\projects\DDD\DDD\Frontend\Blazor\Scheduling.BlazorApp\Models\CreatePatientModel.cs`

```csharp
namespace Scheduling.BlazorApp.Models;

public class CreatePatientModel
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public DateTime? DateOfBirth { get; set; }
}
```

**Design Notes**
- Properties are nullable to support initial empty state
- No validation attributes (separation of concerns)
- Simple DTOs mirror command structure but with nullable types

### Validator Implementation

**File**: `C:\projects\DDD\DDD\Frontend\Blazor\Scheduling.BlazorApp\Validators\CreatePatientModelValidator.cs`

```csharp
using FluentValidation;
using Scheduling.BlazorApp.Models;

namespace Scheduling.BlazorApp.Validators;

public class CreatePatientModelValidator : AbstractValidator<CreatePatientModel>
{
    public CreatePatientModelValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required")
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required")
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress(FluentValidation.Validators.EmailValidationMode.AspNetCoreCompatible);

        RuleFor(x => x.DateOfBirth)
            .NotNull().WithMessage("Date of birth is required")
            .LessThan(DateTime.Today).WithMessage("Date of birth must be in the past");
    }
}
```

**Validation Rules**
- **FirstName/LastName**: Required, maximum length 100 characters
- **Email**: Required, valid email format using ASP.NET Core compatible validation
- **DateOfBirth**: Required, must be in the past

**Custom Messages**
The `WithMessage` method provides user-friendly error messages that appear in the UI.

---

## Setting Up FluentValidation in Blazor

FluentValidation requires package installation and service registration to work with Blazor's validation system.

### Install Required Packages

Add the following packages to the Blazor Server project:

```bash
dotnet add package FluentValidation
dotnet add package Blazored.FluentValidation
```

**Package Purposes**

| Package | Purpose |
|---------|---------|
| FluentValidation | Core validation library |
| Blazored.FluentValidation | Blazor integration (FluentValidationValidator component) |

### Register Validators in Dependency Injection

**File**: `C:\projects\DDD\DDD\Frontend\Blazor\Scheduling.BlazorApp\Program.cs`

```csharp
using FluentValidation;

// Register all validators in the assembly
builder.Services.AddValidatorsFromAssemblyContaining<CreatePatientModelValidator>();
```

**What This Does**
The `AddValidatorsFromAssemblyContaining<T>()` method scans the assembly containing the specified type and registers all `AbstractValidator<T>` implementations as scoped services.

### FluentValidationValidator Component

The `Blazored.FluentValidation` package provides the `<FluentValidationValidator />` component:

```razor
<EditForm Model="@model" OnValidSubmit="HandleValidSubmit">
    <FluentValidationValidator />
    <!-- form fields -->
</EditForm>
```

**How It Works**
1. The component hooks into EditContext's validation system
2. On form submission, it retrieves the appropriate validator from DI
3. It runs validation and populates EditContext's validation messages
4. `<FluentValidationMessage />` components display these messages

---

## Client-Side vs Server-Side Validation

This project implements validation at both client and server layers. Understanding the distinction is critical for security and user experience.

### Validation Strategy

| Layer | Purpose | Technology | Trust Level |
|-------|---------|------------|-------------|
| Client (Blazor) | User experience — immediate feedback | FluentValidation (Blazored.FluentValidation) | Never trusted alone |
| Server (API) | Security — authoritative validation | FluentValidation (MediatR pipeline behavior) | Source of truth |

### Why Both Layers?

**Client-Side Validation**
- Provides immediate feedback without server round-trip
- Reduces unnecessary API calls
- Improves perceived performance
- Enhances user experience

**Server-Side Validation**
- Cannot be bypassed by malicious clients
- Enforces business rules authoritatively
- Protects data integrity
- Required for security

### Validation Flow

```
User Input
    ↓
Client Validator (UX)
    ↓ (if valid)
API Request
    ↓
Server Validator (Security)
    ↓ (if valid)
Command Handler
    ↓
Domain Logic
```

### Backend Validators Are Source of Truth

The backend validators in `Scheduling.Application` define the authoritative rules:

**Example**: `C:\projects\DDD\DDD\Core\Scheduling\Scheduling.Application\Patients\Commands\CreatePatient\CreatePatientCommandValidator.cs`

```csharp
public class CreatePatientCommandValidator : AbstractValidator<CreatePatientCommand>
{
    public CreatePatientCommandValidator(IUnitOfWork unitOfWork)
    {
        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MustAsync(async (email, cancellationToken) =>
            {
                var exists = await unitOfWork.Patients.ExistsAsync(
                    p => p.Email == email, cancellationToken);
                return !exists;
            })
            .WithMessage("A patient with this email already exists");

        RuleFor(x => x.DateOfBirth)
            .NotEmpty()
            .LessThan(DateTime.Today);
    }
}
```

**Key Difference**
The backend validator includes database checks (email uniqueness) that cannot be performed client-side. The frontend validator mirrors basic rules but defers to the server for authoritative validation.

---

## Handling Server Validation Errors

When backend validation fails, the API returns structured error responses. The frontend must capture and display these errors.

### Backend Error Response Format

The `ExceptionToJsonFilter` in `BuildingBlocks.WebApplications` returns validation errors in a consistent format:

```json
{
  "success": false,
  "errors": [
    "First name is required",
    "A patient with this email already exists"
  ]
}
```

### Frontend Error Handling

The form component captures server errors in the `HandleValidSubmit` method:

```csharp
private async Task HandleValidSubmit()
{
    isSubmitting = true;
    serverErrors = null; // Clear previous errors

    try
    {
        var response = await PatientApi.CreatePatientAsync(request);

        if (response.Success)
        {
            // Success path
        }
        else
        {
            // Server validation failed
            serverErrors = response.Errors ?? ["An unknown error occurred"];
        }
    }
    catch (HttpRequestException ex)
    {
        // Network or HTTP error
        serverErrors = [$"Failed to create patient: {ex.Message}"];
    }
    finally
    {
        isSubmitting = false;
    }
}
```

### Error Display

Server errors are displayed using `FluentMessageBar`:

```razor
@if (serverErrors?.Any() == true)
{
    <FluentMessageBar Intent="MessageBarIntent.Error">
        <ul>
            @foreach (var error in serverErrors)
            {
                <li>@error</li>
            }
        </ul>
    </FluentMessageBar>
}
```

**User Experience**
- Client validation prevents unnecessary API calls
- Server validation catches issues missed client-side (e.g., duplicate email)
- Errors are displayed prominently but non-intrusively
- Users can correct errors without losing form data

---

## Form State Management

Blazor's form system provides rich state management capabilities through `EditContext`.

### EditContext Responsibilities

**Field Modification Tracking**
`EditContext` tracks which fields have been modified (`IsModified`). This enables "dirty" form detection and conditional save prompts.

**Validation State**
`EditContext` maintains validation messages for each field and the form as a whole. The `Validate()` method triggers validation on demand.

**Field Identifiers**
Each field is identified by a `FieldIdentifier` (model instance + property name). Validation messages are keyed by field identifier.

### Form Submission Flow

```
User clicks Submit
    ↓
EditForm intercepts submit event
    ↓
EditContext.Validate() called
    ↓
Valid? → OnValidSubmit event fires
Invalid? → OnInvalidSubmit event fires (optional)
```

### Preventing Double Submission

The `isSubmitting` flag prevents double-submission:

```csharp
private bool isSubmitting;

private async Task HandleValidSubmit()
{
    isSubmitting = true; // Disable submit button
    try
    {
        await PatientApi.CreatePatientAsync(request);
    }
    finally
    {
        isSubmitting = false; // Re-enable submit button
    }
}
```

```razor
<FluentButton Type="ButtonType.Submit" Disabled="@isSubmitting">
    @(isSubmitting ? "Creating..." : "Create Patient")
</FluentButton>
```

**Benefits**
- Prevents duplicate API calls
- Provides visual feedback during submission
- Handles slow network conditions gracefully

### Form Reset

To reset a form after successful submission or on cancel:

```csharp
// Option 1: Create new model instance
model = new CreatePatientModel();

// Option 2: Clear existing model
model.FirstName = null;
model.LastName = null;
// ... etc
```

Creating a new model instance is cleaner and ensures no residual state.

---

## Verification Checklist

Use this checklist to verify your forms and validation implementation:

### Package Installation
- [ ] FluentValidation package installed in Blazor project
- [ ] Blazored.FluentValidation package installed in Blazor project

### Validator Setup
- [ ] CreatePatientModel class created in Models folder
- [ ] CreatePatientModelValidator class created in Validators folder
- [ ] Validators registered in Program.cs using AddValidatorsFromAssemblyContaining

### Form Implementation
- [ ] Create Patient form renders with FluentUI form fields
- [ ] FluentValidationValidator component added to EditForm
- [ ] FluentValidationMessage components added for each field
- [ ] Model property binding configured with @bind-Value

### Client-Side Validation
- [ ] Required field validation fires on submit (First Name, Last Name, Email, Date of Birth)
- [ ] Email format validation works
- [ ] Date validation enforces past dates only
- [ ] Validation messages display next to fields
- [ ] Form submission blocked when validation fails

### Server-Side Validation
- [ ] Server-side validation errors captured in HandleValidSubmit
- [ ] Server errors displayed in FluentMessageBar
- [ ] Server errors cleared on subsequent submit attempts
- [ ] Network errors handled gracefully

### Form State Management
- [ ] Submit button disabled during submission
- [ ] Submit button text changes to "Creating..." during submission
- [ ] Double-submission prevented with isSubmitting flag
- [ ] Form state resets after successful creation

### User Experience
- [ ] Successful creation shows success notification
- [ ] Successful creation redirects to patient list
- [ ] Cancel button returns to patient list without submitting
- [ ] Form retains data when server validation fails
- [ ] Error messages are clear and actionable

---

## Navigation

- **Previous**: [04-blazor-state-management.md](./04-blazor-state-management.md)
- **Back to overview**: [../00-frontend-overview.md](../00-frontend-overview.md)
