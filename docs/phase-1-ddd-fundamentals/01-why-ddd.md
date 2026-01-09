# Why DDD? Understanding the Problem

## The Problem: Anemic Domain Models

Traditional enterprise applications often end up with "anemic domain models" - classes that are just data containers with getters and setters, while all business logic lives in service classes.

```csharp
// Anemic Domain Model - The Problem
public class Patient
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
}

public class PatientService
{
    public void UpdateEmail(Patient patient, string newEmail)
    {
        // Where should validation go? Here? Controller? Database?
        patient.Email = newEmail;
        _repository.Save(patient);
    }
}
```

### Problems with This Approach

1. **Business rules are scattered** - Validation might be in controllers, services, or multiple places
2. **No encapsulation** - Anyone can set `Email` to anything, including invalid data
3. **Domain knowledge is lost** - Code doesn't express *why* things happen
4. **Hard to test** - Business logic mixed with infrastructure
5. **Easy to forget rules** - New developers don't know where to look

## The DDD Solution: Rich Domain Models

DDD says: **Put the business rules inside the domain objects themselves.**

```csharp
// Rich Domain Model - The Solution
public class Patient
{
    public PatientId Id { get; }              // Value Object, not primitive
    public Email Email { get; private set; }   // Private setter = encapsulation

    public void ChangeEmail(Email newEmail)    // Behavior, not just data
    {
        // Business rules live HERE, in ONE place
        if (newEmail == Email) return;

        Email = newEmail;
        AddDomainEvent(new PatientEmailChangedEvent(Id, newEmail));
    }
}
```

### Benefits of Rich Domain Models

1. **Business rules in one place** - Inside the domain object
2. **Encapsulation** - Private setters, changes only through methods
3. **Self-documenting** - `ChangeEmail()` tells you what's happening
4. **Domain Events** - Other parts of the system can react to changes
5. **Type safety** - `PatientId` can't be confused with `AppointmentId`
6. **Easy to test** - Domain logic is isolated from infrastructure

## Key Insight: The Domain is the Core

In DDD, we structure our application so the **domain layer has no dependencies**. It doesn't know about:
- Databases
- Web frameworks
- External services

This is called the **Dependency Inversion Principle** - high-level modules (domain) don't depend on low-level modules (database). Both depend on abstractions.

```
┌─────────────────────────────────────┐
│           Presentation              │  (Blazor, API Controllers)
├─────────────────────────────────────┤
│           Application               │  (Use cases, orchestration)
├─────────────────────────────────────┤
│             Domain                  │  (Entities, Value Objects, Events)
│         NO DEPENDENCIES             │  ← The heart of DDD
├─────────────────────────────────────┤
│          Infrastructure             │  (EF Core, RabbitMQ, external APIs)
└─────────────────────────────────────┘
        All arrows point inward
```

## When to Use DDD

DDD is **not** for every project. It adds complexity. Use it when:

- Business logic is complex
- The domain is the competitive advantage
- Long-term maintainability matters
- Multiple teams work on the same system

**Don't use DDD for:**
- Simple CRUD applications
- Prototypes
- Short-lived projects

## Healthcare Domain Example

For our learning project, we're modeling healthcare scheduling. This is a good DDD fit because:

- Complex business rules (appointment conflicts, cancellation policies)
- Multiple bounded contexts (Scheduling, Billing, Medical Records)
- Real-world constraints (doctors have schedules, patients have insurance)

## Next Steps

Now that we understand *why* DDD, we'll learn the building blocks:
1. **Value Objects** - Immutable objects without identity (don't have id domain layer (can still have id in DB if needed...))
2. **Entities** - Objects with identity that change over time (have id in domain and DB)
3. **Aggregates** - Clusters of objects treated as a unit

→ Continue to: [02-clean-architecture.md](./02-clean-architecture.md) - Project Structure
