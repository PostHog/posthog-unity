# Contributing to PostHog Unity SDK

Thank you for your interest in contributing to the PostHog Unity SDK! This document provides guidelines and instructions for development.

## Development Setup

### Prerequisites

- [.NET SDK 8.0+](https://dotnet.microsoft.com/download)
- Unity 2021.3 LTS or later (for testing in-editor)
- Git

### Getting Started

1. Clone the repository:

   ```bash
   git clone https://github.com/PostHog/posthog-unity.git
   cd posthog-unity
   ```

2. Run the bootstrap script to install dependencies:

   ```bash
   bin/bootstrap
   ```

## Project Structure

```text
posthog-unity/
├── com.posthog.unity/          # Unity package (main SDK)
│   ├── Runtime/                # SDK source code
│   ├── Editor/                 # Unity Editor integrations
│   └── Samples~/               # Example code
├── tests/
│   └── PostHog.Unity.Tests/    # Unit tests
├── bin/                        # Development scripts
│   ├── bootstrap               # Install dependencies
│   ├── fmt                     # Format code
│   ├── test                    # Run tests
│   └── release                 # Release automation
└── .github/workflows/          # CI/CD pipelines
```

## Development Workflow

### Running Tests

Run all unit tests:

```bash
bin/test
```

Run tests with a filter:

```bash
bin/test --filter "FeatureFlag"
```

Run tests with verbose output:

```bash
bin/test --verbose
```

### Formatting Code

Format all C# files:

```bash
bin/fmt
```

Check formatting without making changes (used in CI):

```bash
bin/fmt --check
```

The formatter uses:

- `dotnet format` for code style (file-scoped namespaces, etc.)
- [CSharpier](https://csharpier.com/) for whitespace formatting

### Building

Build the SDK to verify it compiles:

```bash
bin/build
```

## Code Style

- Use file-scoped namespaces
- Follow C# naming conventions (PascalCase for public members, camelCase with underscore prefix for private fields)
- Add XML documentation comments for public APIs
- Keep methods focused and short
- Write tests for new functionality

## Pull Request Guidelines

1. **Create a branch** from `main` with a descriptive name
2. **Write tests** for any new functionality
3. **Run tests and formatting** before submitting:

   ```bash
   bin/fmt && bin/test
   ```

4. **Keep commits clean** with clear, concise messages
5. **Update documentation** if you're changing public APIs

### PR Checklist

- [ ] Tests pass (`bin/test`)
- [ ] Code is formatted (`bin/fmt --check`)
- [ ] Documentation updated (if applicable)
- [ ] No breaking changes (or clearly documented if intentional)

## Testing in Unity

To test the SDK in a Unity project:

1. Open Unity Package Manager
2. Click "+" and select "Add package from disk"
3. Navigate to `com.posthog.unity/package.json`
4. The SDK will be imported into your project

## Releasing

Releases are managed through GitHub Actions. See [RELEASING.md](RELEASING.md) for the full release process.

## Getting Help

- Open an issue for bugs or feature requests
- Check existing issues before creating new ones
- Join the [PostHog community Slack](https://posthog.com/slack) for questions

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
