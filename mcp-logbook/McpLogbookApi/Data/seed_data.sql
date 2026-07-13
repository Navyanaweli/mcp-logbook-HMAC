-- Seed data for Companies, Ships, ShipDetail, ShipLogTable
-- Dialect: SQLite
-- Run after schema.sql

-- Companies (declared before Ships since every ship now belongs to exactly one company)
INSERT INTO Companies (CompanyId, CompanyName) VALUES
(1, 'Ocean Star Shipping Co.'),
(2, 'Pacific Container Lines');

-- Ships: Ocean Star Shipping Co. (CompanyId 1) owns MV OCEAN STAR + MV NORTHERN LIGHT,
-- Pacific Container Lines (CompanyId 2) owns MV PACIFIC DAWN
INSERT INTO Ships (ShipId, ShipName, CompanyId) VALUES
(1, 'MV OCEAN STAR', 1),
(2, 'MV NORTHERN LIGHT', 1),
(3, 'MV PACIFIC DAWN', 2);

-- ShipDetail
INSERT INTO ShipDetail (
    ShipId, ShipName, OfficialNumber, ImoNumber, MMSI, CallSign,
    Nationality, PortOfRegistry, FlagState, MainClass, ShipType,
    Owner, Manager, Operators, GrossTon, NetTon, Deadweight,
    SummerDraft, LengthOA, MainEngine, TypeOfEngine,
    DateOfKeelLaid, DateOfDelivery, IsActive
) VALUES
(1, 'MV OCEAN STAR', '100001', '9123456', '403111000', 'HZAA1',
    'PANAMA', 'PANAMA CITY', 'PANAMA', 'ABS', 'Oil Tanker',
    'Ocean Star Shipping Co.', 'Star Ship Management', 'Star Oil Transport',
    50000, 32000, 95000, 14.5, 228.6, 'MAN B&W 6G60ME-C9', 'SLOW SPEED',
    '2015-03-10', '2015-11-20', 1),
(2, 'MV NORTHERN LIGHT', '100002', '9234567', '403222000', 'HZBB2',
    'LIBERIA', 'MONROVIA', 'LIBERIA', 'DNV', 'Bulk Carrier',
    'Northern Light Shipping', 'Northern Ship Management', 'Northern Bulk Transport',
    38000, 24000, 72000, 12.8, 199.9, 'WARTSILA 6RT-flex58T-D', 'SLOW SPEED',
    '2017-06-01', '2018-01-15', 1),
(3, 'MV PACIFIC DAWN', '100003', '9345678', '403333000', 'HZCC3',
    'MARSHALL ISLANDS', 'MAJURO', 'MARSHALL ISLANDS', 'ABS', 'Container Ship',
    'Pacific Dawn Shipping', 'Pacific Ship Management', 'Pacific Container Lines',
    62000, 40000, 88000, 13.2, 260.0, 'MAN B&W 8G70ME-C9', 'SLOW SPEED',
    '2019-02-14', '2019-09-30', 1);

-- ShipLogTable
INSERT INTO ShipLogTable (ShipId, LogText, LogDate) VALUES
(1, 'Departed Panama City port, all systems normal.', '2026-06-01 06:00:00'),
(1, 'Position check at 08°30N 079°10W, weather clear.', '2026-06-01 12:00:00'),
(2, 'Loaded bulk cargo at Monrovia, draft survey completed.', '2026-06-02 09:15:00'),
(2, 'Encountered rough seas, reduced speed to 10 knots.', '2026-06-03 03:40:00'),
(3, 'Arrived at Majuro anchorage, awaiting berth.', '2026-06-04 14:20:00'),
(3, 'Bunkering operation completed, 450 mt FO received.', '2026-06-05 07:50:00');
