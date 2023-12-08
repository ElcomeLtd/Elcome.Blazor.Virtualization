# Elcome.Blazor.Virtualization

Provides functionality for rendering a virtualized list of items. Unlike the built-in `Virtualize` component, `FlexibleVirtualize` can display items with varying heights.

# Usage

1. Register the required services for dependency injection

```cs
services.AddElcomeBlazorVirtualization();
```

2. Use in the same way as the built-in `Virtualize` component

```razor
<FlexibleVirtualize Context="employee" ItemsProvider="@LoadEmployees">
    <p>
        @employee.FirstName @employee.LastName has the 
        job title of @employee.JobTitle.
    </p>
</FlexibleVirtualize>
```
