-- Schema: Companies, Ships, ShipDetail, ShipLogTable
-- Dialect: SQLite

-- External companies. Declared before Ships since every ship now belongs to exactly
-- one company (see Ships.CompanyId below).
CREATE TABLE Companies (
    CompanyId INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyName TEXT NOT NULL
);

-- One-to-many: a company may own many ships, but a ship has exactly one owning company --
-- matching real-world practice (a vessel has a single commercial operator at a time) and
-- ShipDetail.Owner/Manager below, which are already single-valued per ship, not lists.
CREATE TABLE Ships (
    ShipId INTEGER PRIMARY KEY AUTOINCREMENT,
    ShipName TEXT NOT NULL,
    CompanyId INTEGER NOT NULL,
    FOREIGN KEY (CompanyId) REFERENCES Companies(CompanyId)
);

CREATE TABLE ShipDetail (
    ShipDetailId INTEGER PRIMARY KEY AUTOINCREMENT,
    ShipId INTEGER NOT NULL UNIQUE,

    ShipName TEXT NOT NULL,
    OfficialNumber TEXT,
    ImoNumber TEXT,
    MMSI TEXT,
    CallSign TEXT,
    Nationality TEXT,
    PortOfRegistry TEXT,
    FlagState TEXT,

    MainClass TEXT,
    ShipType TEXT,
    Owner TEXT,
    Manager TEXT,
    Operators TEXT,
    RegisteredOwnerUID TEXT,
    UIDOfCompany TEXT,

    GrossTon REAL,
    NetTon REAL,
    Deadweight REAL,
    SummerDraft REAL,
    LengthOA REAL,

    MainEngine TEXT,
    TypeOfEngine TEXT,

    DateOfKeelLaid TEXT,
    DateOfDelivery TEXT,
    FromDateOfRegistryToFlagState TEXT,
    ToDateOfRegistryToFlagState TEXT,

    NameOfBBC TEXT,
    AddressOfBBC TEXT,
    NameOfROIssuingDOC TEXT,
    NameOfROIssuingInternationalShipSecurityCertificate TEXT,
    NameOfROIssuingSafetyManagementCertificate TEXT,
    AddressOfISMManager TEXT,
    AddressOfRegisteredOwner TEXT,

    TimeZoneOffSet TEXT,
    SeaAreas TEXT,
    OtherText TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1,
    IsExported INTEGER NOT NULL DEFAULT 0,
    ExportShipId TEXT,
    ExportUrl TEXT,
    CloudExportDate TEXT,
    CreatedDate TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedDate TEXT,

    FOREIGN KEY (ShipId) REFERENCES Ships(ShipId)
);

CREATE TABLE ShipLogTable (
    ShipLogId INTEGER PRIMARY KEY AUTOINCREMENT,
    ShipId INTEGER NOT NULL,
    LogText TEXT NOT NULL,
    LogDate TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (ShipId) REFERENCES Ships(ShipId)
);

-- ── External (HMAC) client access ──────────────────────────────────────
-- External clients are scoped via Companies -> Ships.CompanyId directly,
-- resolved through ClientCompanyAccess for a given Client.

-- One row per external client credential. ClientId is a GUID stored as TEXT.
-- EncryptedSecret holds the base64 shared secret encrypted via IDataProtector
-- (reversible, NOT a one-way hash) since HMAC verification must recompute the
-- signature from the raw secret. CompanyId is the client's primary/default company.
CREATE TABLE Clients (
    ClientId TEXT PRIMARY KEY,
    CompanyId INTEGER NOT NULL,
    EncryptedSecret TEXT NOT NULL,
    ClientName TEXT NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ExpiresAt TEXT,
    RevokedAt TEXT,
    LastUsedAt TEXT,
    FOREIGN KEY (CompanyId) REFERENCES Companies(CompanyId)
);

-- Many-to-many: which companies a given client is allowed to read data for.
-- No AccessLevel column -- all external access is read-only by design.
CREATE TABLE ClientCompanyAccess (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ClientId TEXT NOT NULL,
    CompanyId INTEGER NOT NULL,
    GrantedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (ClientId) REFERENCES Clients(ClientId),
    FOREIGN KEY (CompanyId) REFERENCES Companies(CompanyId),
    UNIQUE (ClientId, CompanyId)
);

-- Separate from any internal AuditService logging -- exclusively for external
-- HMAC client attempts, queryable only via an admin-only endpoint.
CREATE TABLE ExternalClientAuditLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ClientId TEXT,
    RequestedCompanyId INTEGER,
    ToolName TEXT NOT NULL,
    Allowed INTEGER NOT NULL,
    DenialReason TEXT,
    Timestamp TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
