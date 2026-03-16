USE booking_guardian;

-- 1. เคลียร์ข้อมูลเดิมทิ้งทั้งหมด (Force Reset)
SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS `anomalies`;
DROP TABLE IF EXISTS `audit_logs`;
DROP TABLE IF EXISTS `endpoint_health`;
DROP TABLE IF EXISTS `bookings`;
DROP TABLE IF EXISTS `users`;
SET FOREIGN_KEY_CHECKS = 1;

-- 2. สร้างโครงสร้างโต๊ะใหม่
CREATE TABLE `users` (
    `id`            INT AUTO_INCREMENT PRIMARY KEY,
    `email`         VARCHAR(100) NOT NULL UNIQUE,
    `password_hash` VARCHAR(255) NOT NULL,
    `full_name`     VARCHAR(100) NOT NULL,
    `role`          ENUM('Admin','Supporter') NOT NULL DEFAULT 'Supporter', -- reserved for future use
    `created_at`    DATETIME DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `bookings` (
    `id`               INT AUTO_INCREMENT PRIMARY KEY,
    `reference_no`     VARCHAR(20)  NOT NULL UNIQUE,
    `customer_name`    VARCHAR(100) NOT NULL,
    `route`            VARCHAR(150) NOT NULL,
    `operator_name`    VARCHAR(100) NOT NULL,
    `phone_number`     VARCHAR(20) NULL,
    `passenger_count`  TINYINT      NOT NULL DEFAULT 1,
    `travel_date`      DATE         NOT NULL,
    `amount`           DECIMAL(10,2) NOT NULL,
    `payment_status`   ENUM('PENDING','SUCCESS','FAILED') NOT NULL DEFAULT 'PENDING',
    `booking_status`   ENUM('PENDING','CONFIRMED','CANCELLED','RECOVERED') NOT NULL DEFAULT 'PENDING',
    `payment_at`       DATETIME NULL,
    `created_at`       DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX `idx_anomaly_detection` (`payment_status`, `booking_status`, `payment_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `audit_logs` (
    `id`            INT AUTO_INCREMENT PRIMARY KEY,
    `action`        VARCHAR(100) NOT NULL,
    `entity_type`   VARCHAR(50)  NOT NULL,
    `entity_id`     INT          NOT NULL,
    `performed_by`  VARCHAR(100) NOT NULL,
    `ip_address`    VARCHAR(45)  NULL,
    `user_agent`    VARCHAR(255) NULL,
    `note`          TEXT NULL,
    `detail`        JSON NULL,
    `created_at`    DATETIME DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `endpoint_health` (
    `id`            INT AUTO_INCREMENT PRIMARY KEY,
    `name`          VARCHAR(100) NOT NULL,
    `url`           VARCHAR(255) NOT NULL,
    `status`        ENUM('UP','DEGRADED','DOWN') NOT NULL,
    `response_ms`   INT NULL,
    `http_code`     INT NULL,
    `checked_at`    DATETIME DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `app_logs` (
    `id`              INT AUTO_INCREMENT PRIMARY KEY,
    `Timestamp`       TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `Level`           VARCHAR(15) NOT NULL,
    `Message`         TEXT NOT NULL,
    `MessageTemplate` TEXT NULL,
    `Exception`       TEXT NULL,
    `Properties`      JSON NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `anomalies` (
    `id`                INT AUTO_INCREMENT PRIMARY KEY,
    `booking_id`        INT  NOT NULL,
    `endpoint_health_id` INT NULL,
    `detected_at`       DATETIME DEFAULT CURRENT_TIMESTAMP,
    `detection_run_at`  DATETIME NOT NULL,
    `status`            ENUM('OPEN','RESOLVED','IGNORED') NOT NULL DEFAULT 'OPEN',
    `resolved_at`       DATETIME NULL,
    `resolved_by`       VARCHAR(100) NULL,
    `note`              TEXT NULL,
    CONSTRAINT `fk_booking` FOREIGN KEY (`booking_id`) REFERENCES `bookings`(`id`),
    CONSTRAINT `fk_anomaly_endpoint_health` FOREIGN KEY (`endpoint_health_id`) REFERENCES `endpoint_health`(`id`),
    INDEX `idx_anomaly_endpoint_health` (`endpoint_health_id`),
    UNIQUE KEY `uq_booking_anomaly` (`booking_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 3. INSERT ข้อมูลเริ่มต้น
-- Password: Monitor1234! (Verified BCrypt Hash)
INSERT INTO `users` (`email`, `password_hash`, `full_name`, `role`) VALUES
('admin@monitor.dev', '$2a$12$fqIXQzA/IErEZPLlVVv2MuHC0dcU5BL8QeofOtKtnudaDxFG138Ri', 'Admin User', 'Admin');

INSERT INTO `bookings` (`reference_no`, `customer_name`, `route`, `operator_name`, `phone_number`, `travel_date`, `amount`, `payment_status`, `booking_status`, `payment_at`) VALUES
('BK-999-001', 'Somchai Jaidee', 'Bangkok -> Chiang Mai', 'Nakhonchai Air', '0912345601', CURDATE() + INTERVAL 5 DAY, 850.00, 'SUCCESS', 'PENDING', NOW() - INTERVAL 15 MINUTE),
('BK-999-002', 'Somsri Rakthai', 'Phuket -> Bangkok', 'Bus Express', '0912345602', CURDATE() + INTERVAL 6 DAY, 720.00, 'SUCCESS', 'PENDING', NOW() - INTERVAL 20 MINUTE),
('BK-999-003', 'Anan Wiroj', 'Bangkok -> Khon Kaen', 'Nakhonchai Air', '0912345603', CURDATE() + INTERVAL 3 DAY, 640.00, 'SUCCESS', 'PENDING', NOW() - INTERVAL 25 MINUTE),
('BK-999-004', 'Mali Suthida', 'Chiang Mai -> Bangkok', 'GreenBus', '0912345604', CURDATE() + INTERVAL 4 DAY, 780.00, 'SUCCESS', 'PENDING', NOW() - INTERVAL 30 MINUTE),
('BK-999-005', 'Krit Rattan', 'Udon Thani -> Bangkok', 'Transport Co', '0912345605', CURDATE() + INTERVAL 7 DAY, 910.00, 'SUCCESS', 'PENDING', NOW() - INTERVAL 35 MINUTE),
('BK-999-006', 'Niran Chai', 'Bangkok -> Pattaya', 'Bell Travel', '0912345606', CURDATE() + INTERVAL 2 DAY, 220.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 50 MINUTE),
('BK-999-007', 'Arisa Moon', 'Bangkok -> Hua Hin', 'Roong Reuang', '0912345607', CURDATE() + INTERVAL 8 DAY, 390.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 70 MINUTE),
('BK-999-008', 'Korn Pat', 'Chiang Mai -> Pai', 'GreenBus', '0912345608', CURDATE() + INTERVAL 6 DAY, 340.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 90 MINUTE),
('BK-999-009', 'Pimlada Star', 'Khon Kaen -> Bangkok', 'Transport Co', '0912345609', CURDATE() + INTERVAL 4 DAY, 660.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 110 MINUTE),
('BK-999-010', 'Nop Thep', 'Surat Thani -> Phuket', 'Phantip', '0912345610', CURDATE() + INTERVAL 9 DAY, 520.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 130 MINUTE),
('BK-999-011', 'Lada Bee', 'Phitsanulok -> Bangkok', 'Sombat Tour', '0912345611', CURDATE() + INTERVAL 3 DAY, 450.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 150 MINUTE),
('BK-999-012', 'Wit Sky', 'Ubon Ratchathani -> Bangkok', 'Nakhonchai Air', '0912345612', CURDATE() + INTERVAL 5 DAY, 880.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 170 MINUTE),
('BK-999-013', 'Fah Rain', 'Bangkok -> Rayong', 'Cherdchai', '0912345613', CURDATE() + INTERVAL 7 DAY, 310.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 190 MINUTE),
('BK-999-014', 'Mek Wind', 'Bangkok -> Nakhon Ratchasima', 'Sombat Tour', '0912345614', CURDATE() + INTERVAL 2 DAY, 430.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 210 MINUTE),
('BK-999-015', 'Praew Bell', 'Krabi -> Phuket', 'Phuket Travel', '0912345615', CURDATE() + INTERVAL 8 DAY, 290.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 230 MINUTE),
('BK-999-016', 'Tong Art', 'Bangkok -> Ayutthaya', 'Mini Van Co', '0912345616', CURDATE() + INTERVAL 1 DAY, 180.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 250 MINUTE),
('BK-999-017', 'June Blue', 'Bangkok -> Kanchanaburi', 'Tour Van', '0912345617', CURDATE() + INTERVAL 4 DAY, 260.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 270 MINUTE),
('BK-999-018', 'Palm Jet', 'Trang -> Hat Yai', 'Local Bus', '0912345618', CURDATE() + INTERVAL 3 DAY, 210.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 290 MINUTE),
('BK-999-019', 'Beam Rose', 'Nakhon Si Thammarat -> Surat Thani', 'Express South', '0912345619', CURDATE() + INTERVAL 6 DAY, 240.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 310 MINUTE),
('BK-999-020', 'Ice Sun', 'Bangkok -> Lopburi', 'Mini Van Co', '0912345620', CURDATE() + INTERVAL 5 DAY, 200.00, 'SUCCESS', 'CONFIRMED', NOW() - INTERVAL 330 MINUTE);

INSERT INTO `audit_logs` (`action`, `entity_type`, `entity_id`, `performed_by`, `note`, `detail`) VALUES
('SYSTEM_INIT', 'SYSTEM', 0, 'SYSTEM', 'Initial System Seed', '{"message": "Seed Success"}');

INSERT INTO `endpoint_health` (`name`, `url`, `status`, `response_ms`, `http_code`, `checked_at`) VALUES
('Payment Gateway', 'https://api.stripe.com/health', 'DOWN', NULL, 503, NOW() - INTERVAL 42 MINUTE),
('SMS Service', 'https://api.twilio.com', 'UP', 50, 200, NOW() - INTERVAL 2 MINUTE);

INSERT INTO `anomalies` (`booking_id`, `endpoint_health_id`, `detected_at`, `detection_run_at`, `status`) VALUES
((SELECT id FROM `bookings` WHERE `reference_no` = 'BK-999-001'), (SELECT id FROM `endpoint_health` WHERE `name` = 'Payment Gateway' ORDER BY `checked_at` DESC LIMIT 1), NOW() - INTERVAL 14 MINUTE, NOW() - INTERVAL 14 MINUTE, 'OPEN'),
((SELECT id FROM `bookings` WHERE `reference_no` = 'BK-999-002'), (SELECT id FROM `endpoint_health` WHERE `name` = 'Payment Gateway' ORDER BY `checked_at` DESC LIMIT 1), NOW() - INTERVAL 19 MINUTE, NOW() - INTERVAL 19 MINUTE, 'OPEN'),
((SELECT id FROM `bookings` WHERE `reference_no` = 'BK-999-003'), (SELECT id FROM `endpoint_health` WHERE `name` = 'Payment Gateway' ORDER BY `checked_at` DESC LIMIT 1), NOW() - INTERVAL 24 MINUTE, NOW() - INTERVAL 24 MINUTE, 'OPEN'),
((SELECT id FROM `bookings` WHERE `reference_no` = 'BK-999-004'), (SELECT id FROM `endpoint_health` WHERE `name` = 'Payment Gateway' ORDER BY `checked_at` DESC LIMIT 1), NOW() - INTERVAL 29 MINUTE, NOW() - INTERVAL 29 MINUTE, 'OPEN'),
((SELECT id FROM `bookings` WHERE `reference_no` = 'BK-999-005'), (SELECT id FROM `endpoint_health` WHERE `name` = 'Payment Gateway' ORDER BY `checked_at` DESC LIMIT 1), NOW() - INTERVAL 34 MINUTE, NOW() - INTERVAL 34 MINUTE, 'OPEN');
