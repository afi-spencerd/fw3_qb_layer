# FormulaWeb → QuickBooks Integration  
## Developer Specification: Integration Tables & Fields

---

# 1. Overview

This document defines the **data model and table structures** for integrating FormulaWeb with QuickBooks Desktop via a staging-based (Web Connector / SDK) architecture.

### Key Design Constraints
- QuickBooks uses **pull-based integration**
- FormulaWeb must **stage outbound data**
- Current system aggregates data; future system will use **transaction-level events**
- Integration must support:
  - Replay
  - Audit
  - Idempotency
  - Cutover (RM → WIP → FG)

---

# 2. Core Integration Tables

---

## 2.1 `fw_qb_integration_event`

### Purpose
Primary outbound transaction table from FormulaWeb to QuickBooks.

```sql
CREATE TABLE fw_qb_integration_event (
    EventID BIGINT PRIMARY KEY,
    SourceSystem VARCHAR(50) DEFAULT 'FormulaWeb',

    -- Business Context
    TransactionType VARCHAR(50) NOT NULL,
    WorkOrderID BIGINT NULL,
    SalesOrderID BIGINT NULL,
    LotID BIGINT NULL,
    ItemID BIGINT NOT NULL,

    -- Inventory Movement
    FromStatus VARCHAR(20),
    ToStatus VARCHAR(20),
    Quantity DECIMAL(18,6) NOT NULL,
    UOM VARCHAR(20) NOT NULL,

    -- Timing
    AccountingDate DATE NOT NULL,
    OperationalTimestamp DATETIME NOT NULL,

    -- Financial
    CostAmount DECIMAL(18,4) NULL,

    -- Integration Control
    PostingBatchID BIGINT NULL,
    QB_TransactionID VARCHAR(100) NULL,

    -- Status Tracking
    Status VARCHAR(20) DEFAULT 'PENDING',
    RetryCount INT DEFAULT 0,
    LastError TEXT NULL,

    CreatedAt DATETIME DEFAULT GETDATE(),
    UpdatedAt DATETIME NULL
);
```

---

## 2.2 `fw_qb_posting_batch`

```sql
CREATE TABLE fw_qb_posting_batch (
    PostingBatchID BIGINT PRIMARY KEY,
    BatchDate DATE NOT NULL,
    BatchType VARCHAR(50),
    Status VARCHAR(20),
    CreatedAt DATETIME DEFAULT GETDATE(),
    PostedAt DATETIME NULL
);
```

---

## 2.3 `fw_qb_request_audit`

```sql
CREATE TABLE fw_qb_request_audit (
    AuditID BIGINT PRIMARY KEY,
    EventID BIGINT NOT NULL,
    RequestXML TEXT NOT NULL,
    ResponseXML TEXT NULL,
    QB_Status VARCHAR(20),
    QB_ErrorCode VARCHAR(50),
    QB_ErrorMessage TEXT,
    CreatedAt DATETIME DEFAULT GETDATE()
);
```

---

## 2.4 `fw_qb_mapping_account`

```sql
CREATE TABLE fw_qb_mapping_account (
    MappingID INT PRIMARY KEY,
    TransactionType VARCHAR(50) NOT NULL,
    DebitAccount VARCHAR(100) NOT NULL,
    CreditAccount VARCHAR(100) NOT NULL,
    IsInventoryImpact BIT DEFAULT 1,
    IsCOGSImpact BIT DEFAULT 0
);
```

---

## 2.5 `fw_qb_mapping_item`

```sql
CREATE TABLE fw_qb_mapping_item (
    ItemID BIGINT PRIMARY KEY,
    QB_ItemName VARCHAR(100),
    QB_ItemType VARCHAR(50),
    Active BIT DEFAULT 1,
    CreatedAt DATETIME DEFAULT GETDATE()
);
```

---

## 2.6 `fw_qb_status_log`

```sql
CREATE TABLE fw_qb_status_log (
    LogID BIGINT PRIMARY KEY,
    EventID BIGINT NOT NULL,
    OldStatus VARCHAR(20),
    NewStatus VARCHAR(20),
    ChangedAt DATETIME DEFAULT GETDATE(),
    ChangedBy VARCHAR(50)
);
```

---

## 2.7 `fw_qb_replay_queue`

```sql
CREATE TABLE fw_qb_replay_queue (
    ReplayID BIGINT PRIMARY KEY,
    EventID BIGINT NOT NULL,
    ReplayReason VARCHAR(100),
    ReplayStatus VARCHAR(20) DEFAULT 'PENDING',
    CreatedAt DATETIME DEFAULT GETDATE(),
    ProcessedAt DATETIME NULL
);
```

---

# End of Specification
