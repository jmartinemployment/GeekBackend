# GeekBackend Project Plan: OAuth 2.1/OIDC Server with Real-Time Sync

## Executive Summary

**Project**: GeekBackend - Professional 3-Tier Identity & Sync Engine  
**Architecture**: .NET 10, Dapper, OpenIddict, WebSocket Real-Time Communication  
**Scope**: OAuth 2.1/OIDC Server + Multi-Device Synchronization + Electron Desktop Integration

---

## Project Overview

### Core Capabilities
- **OAuth 2.1/OIDC Identity Server**: Complete authorization server with PKCE, exact URI matching
- **Two-Factor Authentication**: TOTP-based 2FA with recovery codes
- **Device Fingerprinting**: Electron BIOS ID capture for trusted device management
- **Real-Time Synchronization**: WebSocket-based bi-directional communication between devices
- **Multi-Device Support**: Desktop (Electron) ↔ Mobile synchronization via sync queue

### Target Users
- Desktop applications (Electron-based)
- Mobile applications (future)
- Third-party integrations via OAuth 2.1/OIDC

---

## Architecture Overview

### 4-Tier Architecture (.NET Projects)

```
GeekBackend.slnx
├── GeekBackend.Core/         ← Domain contracts & entities
│   ├── Entities/             ← User, Device, Session models
│   ├── Repositories/         ← Interface contracts
│   └── Services/             ← Interface contracts
│
├── GeekBackend.Data/         ← Infrastructure/Persistence
│   ├── Repositories/         ← Dapper implementations
│   ├── Migrations/           ← Database schema management
│   └── Configurations/       ← Connection & EF configs
│
├── GeekBackend.Services/     ← Business Logic Layer
│   ├── Auth/                 ← OAuth/OIDC business logic
│   ├── Sync/                 ← Real-time sync orchestration
│   ├── Identity/             ← User management & 2FA
│   └── Dtos/                 ← Data transfer objects
│
└── GeekBackend.Api/          ← Presentation Layer
    ├── Controllers/          ← HTTP API endpoints
    ├── Middleware/           ← Auth, WebSocket handling
    ├── Hubs/                 ← SignalR WebSocket hubs
    └── Properties/
```

### Database Schema (PostgreSQL + Dapper)

#### 1. OpenIddict Standard Tables (Operational & Configuration)
```sql
-- Applications: OIDC client configurations
CREATE TABLE OpenIddictApplications (
    Id NVARCHAR(450) PRIMARY KEY,
    ClientId NVARCHAR(100) UNIQUE NOT NULL,
    ClientSecret NVARCHAR(MAX),
    DisplayName NVARCHAR(MAX),
    RedirectUris NVARCHAR(MAX),     -- OAuth 2.1 exact matching required
    PostLogoutRedirectUris NVARCHAR(MAX),
    Permissions NVARCHAR(MAX),      -- PKCE requirements
    Requirements NVARCHAR(MAX),
    Type NVARCHAR(50) NOT NULL      -- 'public' or 'confidential'
);

-- Scopes: Permission definitions
CREATE TABLE OpenIddictScopes (
    Id NVARCHAR(450) PRIMARY KEY,
    Name NVARCHAR(200) UNIQUE NOT NULL,
    DisplayName NVARCHAR(MAX),
    Resources NVARCHAR(MAX)         -- JSON array of resources
);

-- Authorizations: User-App relationships
CREATE TABLE OpenIddictAuthorizations (
    Id NVARCHAR(450) PRIMARY KEY,
    ApplicationId NVARCHAR(450) REFERENCES OpenIddictApplications(Id),
    Subject NVARCHAR(450) NOT NULL, -- User's OIDC subject
    Scopes NVARCHAR(MAX),
    Status NVARCHAR(50) NOT NULL,
    Type NVARCHAR(50) NOT NULL,
    CreationDate DATETIME2 NOT NULL
);

-- Tokens: Access/Refresh/Auth codes
CREATE TABLE OpenIddictTokens (
    Id NVARCHAR(450) PRIMARY KEY,
    ApplicationId NVARCHAR(450) REFERENCES OpenIddictApplications(Id),
    AuthorizationId NVARCHAR(450) REFERENCES OpenIddictAuthorizations(Id),
    Subject NVARCHAR(450) NOT NULL,
    Type NVARCHAR(50) NOT NULL,      -- 'access_token', 'refresh_token', etc.
    Payload NVARCHAR(MAX),           -- Stores PKCE challenges internally
    ReferenceId NVARCHAR(100) UNIQUE,
    Status NVARCHAR(50) NOT NULL,
    ExpirationDate DATETIME2
);
```

#### 2. Identity & Security Tables
```sql
-- Core user identity
CREATE TABLE Users (
    Id NVARCHAR(450) PRIMARY KEY,
    Subject NVARCHAR(450) UNIQUE NOT NULL, -- OIDC 'sub' claim
    Username NVARCHAR(256) UNIQUE NOT NULL,
    Email NVARCHAR(256),
    PasswordHash NVARCHAR(MAX) NOT NULL,
    TwoFactorEnabled BIT DEFAULT 0,
    TwoFactorSecret NVARCHAR(MAX),    -- TOTP secret for authenticator apps
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    LastLogin DATETIME2
);

-- Device fingerprinting for Electron
CREATE TABLE Devices (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId NVARCHAR(450) NOT NULL REFERENCES Users(Id),
    DeviceType NVARCHAR(20) NOT NULL, -- 'desktop', 'mobile', 'tablet'
    DeviceName NVARCHAR(100),
    BiosId NVARCHAR(500) NOT NULL,    -- Electron BIOS fingerprint
    LastSeen DATETIME2 DEFAULT GETDATE(),
    IsTrusted BIT DEFAULT 0,
    CONSTRAINT UC_User_Bios UNIQUE (UserId, BiosId)
);
```

#### 3. Real-Time Sync Tables
```sql
-- Active WebSocket connections
CREATE TABLE WebSocketSessions (
    SessionId NVARCHAR(450) PRIMARY KEY, -- Socket connection ID
    UserId NVARCHAR(450) NOT NULL REFERENCES Users(Id),
    DeviceId UNIQUEIDENTIFIER NOT NULL REFERENCES Devices(Id),
    ServerNode NVARCHAR(100) NOT NULL,  -- Backend instance for scaling
    ConnectedAt DATETIME2 DEFAULT GETDATE(),
    LastActivity DATETIME2 DEFAULT GETDATE()
);

-- Offline sync queue
CREATE TABLE SyncQueue (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId NVARCHAR(450) NOT NULL,
    TargetDeviceId UNIQUEIDENTIFIER NOT NULL REFERENCES Devices(Id),
    Payload NVARCHAR(MAX) NOT NULL,      -- JSON sync data
    Status NVARCHAR(20) DEFAULT 'pending', -- 'pending', 'delivered', 'failed'
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    DeliveredAt DATETIME2,
    RetryCount INT DEFAULT 0
);
```

---

## Implementation Phases

### Phase 1: Foundation (Core + Data Projects)
**Duration**: 2-3 weeks  
**Goal**: Establish clean architecture foundation

#### Tasks:
1. **Create Project Structure**
   - [ ] `GeekBackend.Core` project (entities, interfaces)
   - [ ] `GeekBackend.Data` project (Dapper repositories)
   - [ ] Update solution file

2. **Database Schema Implementation**
   - [ ] Create OpenIddict tables
   - [ ] Create Identity tables (Users, Devices)
   - [ ] Create Sync tables (WebSocketSessions, SyncQueue)
   - [ ] Implement DbUp migrations for schema management

3. **Core Domain Models**
   - [ ] User, Device, WebSocketSession entities
   - [ ] Repository interfaces (IUserRepository, IDeviceRepository, etc.)
   - [ ] Service interfaces (IIdentityService, IAuthService, ISyncService)

4. **Data Layer (Dapper)**
   - [ ] UserRepository implementation
   - [ ] DeviceRepository implementation
   - [ ] WebSocketSessionRepository implementation
   - [ ] SyncQueueRepository implementation

### Phase 2: Authentication & Authorization
**Duration**: 3-4 weeks  
**Goal**: OAuth 2.1/OIDC server functionality

#### Tasks:
1. **OpenIddict Integration**
   - [ ] Configure OpenIddict in API project
   - [ ] Implement client registration endpoints
   - [ ] Implement authorization code flow with PKCE
   - [ ] Implement token endpoints

2. **Identity Services**
   - [ ] User registration/login service
   - [ ] Password hashing (ASP.NET Core Identity)
   - [ ] TOTP 2FA implementation
   - [ ] Recovery codes generation

3. **Device Management**
   - [ ] BIOS ID capture and validation
   - [ ] Trusted device registration
   - [ ] Device authentication middleware

### Phase 3: Real-Time Synchronization
**Duration**: 2-3 weeks  
**Goal**: WebSocket communication and sync

#### Tasks:
1. **WebSocket Infrastructure**
   - [ ] SignalR hub implementation
   - [ ] WebSocket session management
   - [ ] Connection authentication with Bearer tokens

2. **Sync Engine**
   - [ ] Sync queue processing
   - [ ] Real-time message broadcasting
   - [ ] Offline message queuing
   - [ ] Conflict resolution strategies

3. **Cross-Device Communication**
   - [ ] Desktop ↔ Mobile sync protocols
   - [ ] Device presence tracking
   - [ ] Connection state management

### Phase 4: Electron Integration
**Duration**: 2 weeks  
**Goal**: Desktop application integration

#### Tasks:
1. **Electron Client Setup**
   - [ ] BIOS ID capture (node-machine-id)
   - [ ] OAuth 2.1/PKCE flow implementation
   - [ ] WebSocket connection with Bearer auth

2. **Desktop Sync Features**
   - [ ] Real-time data synchronization
   - [ ] Offline queue processing
   - [ ] Device trust management

### Phase 5: Security & Production
**Duration**: 2-3 weeks  
**Goal**: Security hardening and deployment

#### Tasks:
1. **Security Hardening**
   - [ ] Rate limiting implementation
   - [ ] Audit logging
   - [ ] Token revocation handling
   - [ ] Security headers (CSP, HSTS)

2. **Production Deployment**
   - [ ] Docker containerization
   - [ ] Database migration scripts
   - [ ] Health checks and monitoring
   - [ ] CI/CD pipeline setup

---

## Technical Requirements

### Dependencies
- **.NET 10**: Target framework
- **OpenIddict**: OAuth 2.1/OIDC server
- **Dapper**: Data access (no EF for auth tables)
- **Entity Framework Core**: Existing business data
- **SignalR**: WebSocket real-time communication
- **PostgreSQL**: Primary database
- **Redis**: Session storage (optional, can use DB)

### Security Requirements
- **OAuth 2.1 Compliance**: PKCE mandatory, exact redirect URI matching
- **TOTP 2FA**: RFC 6238 compliant
- **Device Fingerprinting**: BIOS ID validation
- **Token Security**: Proper expiration, revocation
- **Transport Security**: HTTPS mandatory

### Performance Requirements
- **Concurrent Connections**: Support 1000+ WebSocket connections
- **Token Validation**: Sub-100ms response times
- **Sync Latency**: Real-time message delivery (<500ms)
- **Database Performance**: Efficient queries for auth flows

---

## Risk Assessment

### High Risk Items
1. **OAuth 2.1 Compliance**: Complex security requirements, must pass certification
2. **Real-Time Sync**: WebSocket scaling and conflict resolution
3. **Device Security**: BIOS fingerprinting reliability across platforms

### Mitigation Strategies
1. **Compliance Testing**: Use OAuth 2.1 test suites and OpenID certification
2. **Incremental Implementation**: Start with basic sync, add complexity gradually
3. **Cross-Platform Testing**: Validate BIOS ID capture on multiple OS versions

---

## Success Criteria

### Functional Requirements
- [ ] Complete OAuth 2.1/OIDC authorization server
- [ ] TOTP 2FA with recovery codes
- [ ] Device fingerprinting and trust management
- [ ] Real-time WebSocket synchronization
- [ ] Electron desktop application integration
- [ ] Multi-device offline sync queue

### Non-Functional Requirements
- [ ] <100ms token validation latency
- [ ] 99.9% uptime for auth services
- [ ] Support 1000+ concurrent WebSocket connections
- [ ] <500ms real-time message delivery
- [ ] OAuth 2.1 certification compliance

---

## Team Requirements

### Skills Needed
- **.NET Core/10**: Advanced C# development
- **OAuth 2.1/OIDC**: Security protocol expertise
- **Dapper**: Micro-ORM data access patterns
- **WebSocket/SignalR**: Real-time communication
- **Electron**: Desktop application development
- **PostgreSQL**: Database design and optimization

### Recommended Team Size
- **Phase 1-2**: 2-3 developers (architect + 2 developers)
- **Phase 3-4**: 3-4 developers (add real-time/sync specialist)
- **Phase 5**: 2-3 developers (focus on security/deployment)

---

## Timeline & Milestones

| Phase | Duration | Key Deliverables | Dependencies |
|-------|----------|------------------|--------------|
| Foundation | 2-3 weeks | Core/Data projects, DB schema | None |
| Auth & AuthZ | 3-4 weeks | OAuth server, 2FA, device mgmt | Phase 1 |
| Real-Time Sync | 2-3 weeks | WebSocket, sync engine | Phase 2 |
| Electron Integration | 2 weeks | Desktop client, sync features | Phase 3 |
| Security & Production | 2-3 weeks | Security hardening, deployment | Phase 4 |

**Total Timeline**: 11-15 weeks  
**Go-Live Date**: Target 4 months from project start

---

## Budget Considerations

### Development Costs
- **Senior .NET Developer**: $120k-150k/year × 4 months × 3 devs = $360k-540k
- **Security Specialist**: $140k-180k/year × 2 months = $23k-30k
- **DevOps Engineer**: $130k-160k/year × 1 month = $11k-13k

### Infrastructure Costs
- **PostgreSQL Database**: $500-2000/month (managed)
- **Redis Cache**: $100-500/month (optional)
- **SSL Certificates**: $100-500/year
- **Cloud Hosting**: $200-1000/month (depending on scale)

### Total Estimated Cost: $400k-600k

---

## Next Steps

1. **Project Kickoff**: Assemble team, setup development environment
2. **Architecture Review**: Validate 4-tier approach with security experts
3. **Database Design**: Finalize schema with performance testing
4. **Security Audit**: Third-party review of OAuth implementation plan
5. **Phase 1 Planning**: Detailed task breakdown and sprint planning

---

*Document Version: 1.0*  
*Last Updated: May 6, 2026*  
*Prepared by: AI Planning Assistant*</content>
<parameter name="filePath">/Volumes/Seagate/development/GeekBackend/claude/PROJECT_PLAN.md