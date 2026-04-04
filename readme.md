# NotesApp

A production-grade, mobile-first calendar and notes management application with sophisticated offline capabilities, multi-device synchronization, and comprehensive authentication through Microsoft Entra External ID.

## Overview

NotesApp is designed as a local-first system with enterprise-grade architecture, allowing users to create, organize, and synchronize notes and calendar events across multiple devices seamlessly, even when offline.

### Key Features

- **Multi-Provider Authentication**: Sign in with Google, Microsoft, Apple, Facebook, or email/password through Microsoft Entra External ID
- **Local-First Architecture**: Full functionality offline with intelligent synchronization when online
- **Multi-Device Sync**: Notes and calendars stay in sync across all your devices with conflict detection and resolution
- **Rich Note Taking**: Support for text, attachments, and structured data
- **Calendar Integration**: Calendar views for note and event organization
- **Reminder System**: Reliable reminders that work across devices
- **Enterprise Security**: Comprehensive authentication, authorization, and data protection

## Technology Stack

### Backend
- **.NET 10** with **ASP.NET Core**
- **Entity Framework Core** for data access with atomic transactions
- **CQRS with MediatR** for command and query handling
- **FluentValidation** for request validation
- **FluentResults** for consistent error handling
- **Azure Blob Storage** for asset management (with Managed Identity in production)
- **SQL Server** for relational data with support for vector embeddings
- **Polly** for resilience patterns (retry, circuit breaker, timeout) in worker processes

### Frontend
- **React Native** for iOS and Android
- **MSAL** (Microsoft Authentication Library) for mobile authentication
- Local-first data management with offline support

### Infrastructure & Services
- **Microsoft Entra External ID** for identity and authentication
- **Azure Blob Storage** for file and asset storage
- **Azure SQL Database** for production data persistence
- **Background Workers** for reliable message processing and reminders

## Architecture

NotesApp follows **Clean Architecture** principles with clear separation of concerns:

```
NotesApp.Domain              → Core business logic and entities
NotesApp.Application         → CQRS handlers, DTOs, validators
NotesApp.Infrastructure      → Database, external services, identity
NotesApp.Api                 → HTTP endpoints and request handling
NotesApp.Worker              → Background jobs and message processing
```

## Development Guidelines

**All developers should read [CODING_PRINCIPLES.md](./CODING_PRINCIPLES.md) before contributing code.**

This document outlines:
- Code quality standards and framework best practices
- Entity retrieval patterns and database practices
- Self-documenting code expectations
- Refactoring guidelines
- Architecture and design patterns used across the project

## Project Structure

```
NotesApp/
├── NotesApp.Api/                    # ASP.NET Core API endpoints
├── NotesApp.Application/            # Application layer (CQRS, validators, DTOs)
├── NotesApp.Domain/                 # Domain entities and business logic
├── NotesApp.Infrastructure/         # Infrastructure (EF Core, repositories, services)
├── NotesApp.Worker/                 # Background worker services
├── NotesApp.EntraConsoleTest/       # Console app for testing authentication
│
├── *Tests/                          # Unit and integration tests
│
├── CODING_PRINCIPLES.md             # Development standards and conventions
├── README.md                        # This file
├── NotesApp.sinx                    # Visual Studio Solution file
├── .gitignore                       # Git ignore rules
└── .gitattributes                   # Git attributes
```

## Getting Started

### Prerequisites

- **.NET 10 SDK** or later
- **Visual Studio 2022** (or VS Code with C# extensions)
- **SQL Server** (LocalDB for development)
- **Azure subscription** (for cloud deployment)
- **Microsoft Entra External ID** tenant

### Local Development Setup

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd NotesApp
   ```

2. **Open the solution**
   ```bash
   # Using Visual Studio
   start NotesApp.sln
   
   # Or using command line
   dotnet open NotesApp.sln
   ```

3. **Configure local database**
   - Update connection strings in `appsettings.Development.json`
   - Run Entity Framework migrations:
     ```bash
     dotnet ef database update --project NotesApp.Infrastructure --startup-project NotesApp.Api
     ```

4. **Configure Microsoft Entra External ID**
   - Set up your External ID tenant in Azure Portal
   - Update client ID, tenant ID, and API scope in configuration
   - See configuration documentation for detailed setup

5. **Run the API**
   ```bash
   dotnet run --project NotesApp.Api
   ```
   The API will be available at `https://localhost:7001` (or configured port)

6. **Run background worker** (in a separate terminal)
   ```bash
   dotnet run --project NotesApp.Worker
   ```

## Key Concepts

### System Architecture & Data Flow

NotesApp follows a **command-query segregation** pattern with background processing:

1. **API Request** → ASP.NET Core endpoint receives HTTP request
2. **CQRS Handler** → MediatR routes to Command/Query handler in Application layer
3. **Validation** → FluentValidation pipeline validates request
4. **Persistence** → EF Core writes entity changes + outbox messages in atomic transaction
5. **Cache Invalidation** → Calendar cache is invalidated for consistency
6. **Background Worker** → Separate worker process polls for outbox messages
7. **Async Processing** → Worker processes messages (e.g., AI summaries, embeddings)

**Example: Creating a Task**
- Frontend sends `POST /api/tasks` (optimistic UI shows task immediately)
- Backend validates, creates TaskItem entity + "TaskCreated" outbox message
- Both are saved atomically in a single transaction
- Cache is invalidated so next month view request rebuilds accurate counts
- Worker eventually processes "TaskCreated" message (if any background work is configured)

### Local-First Architecture

NotesApp prioritizes local data availability and synchronization:
- All data is stored locally on the device
- Changes are synced to the server when connectivity is available
- Offline changes are merged with server changes using conflict detection
- Version numbers and device tracking enable reliable conflict resolution

### CQRS Pattern

Commands and Queries are separated for clarity and scalability:
- **Commands**: Modify application state (CreateNote, UpdateNote, DeleteNote)
- **Queries**: Read application state (GetNote, ListNotes, GetCalendarEvents)
- All handlers go through the MediatR pipeline for validation and cross-cutting concerns

### Sync System

The synchronization system ensures data consistency across devices:
- Each device is registered with a unique device ID
- Entity versions track changes for conflict detection
- Reminders are tracked separately to prevent blocking
- **Outbox pattern** ensures reliable background processing: every command creates outbox messages alongside entity changes in a single atomic transaction
- **Cache abstraction** (ICalendarCache) provides in-process or distributed caching for calendar views
- Worker processes continuously poll for unprocessed outbox messages and execute background work

## Building for Production

### API Deployment
```bash
dotnet publish -c Release NotesApp.Api
```

### Configuration
Production deployments require:
- SQL Server production instance
- Azure Blob Storage with Managed Identity authentication
- Microsoft Entra External ID properly configured
- Environment-specific `appsettings.Production.json`

## Testing

Run all tests:
```bash
dotnet test
```

Run specific project tests:
```bash
dotnet test NotesApp.Application.Tests
dotnet test NotesApp.Worker.Tests
```

## Contributing

1. Create a feature branch
2. Ensure your code follows [CODING_PRINCIPLES.md](./CODING_PRINCIPLES.md)
3. Write tests for new functionality
4. Submit a pull request with a clear description of changes
5. Ensure all tests pass and code is reviewed before merging

## Roadmap

### In Progress
- Push notifications via FCM/APNs
- Enhanced conflict resolution with per-field merging
- Comprehensive observability (OpenTelemetry, Serilog)

### Planned
- Semantic search with Azure OpenAI embeddings
- Intelligent note summarization
- Infrastructure as Code (Bicep templates)
- API rate limiting and versioning
- Advanced operational monitoring

## Troubleshooting

### Common Issues

**"Connection string not found"**
- Ensure `appsettings.Development.json` exists in `NotesApp.Api`
- Verify SQL Server LocalDB is running

**"Authentication fails locally"**
- Verify Microsoft Entra External ID configuration
- Check that client ID and tenant ID are correct in `appsettings.Development.json`
- Ensure localhost redirect URIs are registered in External ID

**"Migrations fail"**
- Ensure database exists: `dotnet ef database create`
- Check EF Core is installed: `dotnet tool install -g dotnet-ef`

## Support

For questions or issues:
1. Check the [CODING_PRINCIPLES.md](./CODING_PRINCIPLES.md) for development standards
2. Review relevant documentation in `/docs` (when created)
3. Open an issue in the repository
4. Contact the development team

## License

[Add your license information here]

## Acknowledgments

Built with attention to:
- Clean Architecture principles
- Production-grade security practices
- Mobile-first user experience
- Comprehensive developer experience