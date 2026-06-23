# Contributing

## Development

Install pinned local tools:

```powershell
dotnet tool restore
```

Restore, build, and format before submitting changes:

```powershell
dotnet restore
dotnet build
dotnet csharpier format .
```

## Commit Messages

Use Conventional Commits:

```text
<type>(optional-scope): <description>
```

Common types:

- `feat`: new behavior
- `fix`: bug fix
- `docs`: documentation only
- `style`: formatting only
- `refactor`: code change without behavior change
- `test`: tests only
- `chore`: maintenance

Examples:

```text
feat: add assembly output path option
fix: preserve entry point declaring type name
docs: document csharpier usage
```

Keep commits focused. Use imperative mood in the description.
