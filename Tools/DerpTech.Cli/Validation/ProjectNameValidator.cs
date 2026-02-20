namespace DerpTech.Cli.Validation;

public static class ProjectNameValidator
{
    private static readonly string[] ReservedNames =
    [
        "BaseTemplate",
        "Shared",
        "Tools",
        "Services",
        "Docs",
        "Scripts"
    ];

    public static (bool IsValid, string? Error) Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "Project name cannot be empty");
        }

        if (!char.IsLetter(name[0]))
        {
            return (false, "Project name must start with a letter");
        }

        if (name.Any(c => !char.IsLetterOrDigit(c)))
        {
            return (false, "Project name can only contain letters and digits");
        }

        if (name.Length > 50)
        {
            return (false, "Project name cannot exceed 50 characters");
        }

        if (name.Length < 2)
        {
            return (false, "Project name must be at least 2 characters");
        }

        if (ReservedNames.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, $"'{name}' is a reserved name and cannot be used");
        }

        return (true, null);
    }
}
