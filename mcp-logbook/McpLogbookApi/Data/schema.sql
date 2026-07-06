-- Schema: Users, Ships, UserShipRelationship, ShipDetail, ShipLogTable
-- Dialect: SQLite

CREATE TABLE Users (
    UserId INTEGER PRIMARY KEY AUTOINCREMENT,
    UserName TEXT NOT NULL,
    Email TEXT NOT NULL UNIQUE
);

CREATE TABLE Ships (
    ShipId INTEGER PRIMARY KEY AUTOINCREMENT,
    ShipName TEXT NOT NULL
);

CREATE TABLE UserShipRelationship (
    UserShipRelationshipId INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    ShipId INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (UserId) REFERENCES Users(UserId),
    FOREIGN KEY (ShipId) REFERENCES Ships(ShipId),
    UNIQUE (UserId, ShipId)
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
