# End-to-End CSFLE Implementation Guide
## Two Separate Applications with Encrypted Kafka Messages

**Updated:** February 2026  
**Scenario:** Application A and Application B - two completely independent applications, each with their own producer AND consumer. They communicate via Kafka with encrypted messages. Each application maintains its own secrets and credentials - NO secrets are shared between applications.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Secrets Separation Strategy](#secrets-separation-strategy)
3. [Prerequisites](#prerequisites)
4. [Schema Registry Setup](#schema-registry-setup)
5. [Azure Key Vault Setup](#azure-key-vault-setup)
6. [Application A: Producer Implementation](#application-a-producer-implementation)
7. [Application B: Consumer Implementation](#application-b-consumer-implementation)
8. [Testing the Flow](#testing-the-flow)
9. [Troubleshooting](#troubleshooting)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     Shared Infrastructure                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │    Kafka     │  │   Schema     │  │   Shared KEK in      │  │
│  │   Cluster    │  │   Registry   │  │   Azure Key Vault    │  │
│  └──────────────┘  └──────────────┘  └──────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
         ↑ ↓                 ↑                      ↑
         │ │                 │                      │
         │ │                 │                      │
┌────────┴─┴───────┐  ┌──────┴──────┐  ┌───────────┴──────────┐
│ Application A    │  │Application A│  │ Application A        │
│ (Team 1)         │  │ Schema Reg  │  │ Managed Identity     │
│ ┌──────────────┐ │  │ API Key     │  │ (KEK Access)         │
│ │  Producer    │ │  │             │  │                      │
│ │  Consumer    │ │  │ (READ+WRITE)│  │                      │
│ └──────────────┘ │  │             │  │                      │
│ ┌──────────────┐ │  │             │  │                      │
│ │Own Secrets:  │ │  │             │  │                      │
│ │- Kafka creds │ │  │             │  │                      │
│ │- Schema key  │ │  │             │  │                      │
│ │- Own KV      │ │  │             │  │                      │
│ └──────────────┘ │  │             │  │                      │
└──────────────────┘  └─────────────┘  └──────────────────────┘
         ↑ ↓                 ↑                      ↑
         │ │                 │                      │
         │ │                 │                      │
┌────────┴─┴───────┐  ┌──────┴──────┐  ┌───────────┴──────────┐
│ Application B    │  │Application B│  │ Application B        │
│ (Team 2)         │  │ Schema Reg  │  │ Managed Identity     │
│ ┌──────────────┐ │  │ API Key     │  │ (KEK Access)         │
│ │  Producer    │ │  │             │  │                      │
│ │  Consumer    │ │  │ (READ+WRITE)│  │                      │
│ └──────────────┘ │  │             │  │                      │
│ ┌──────────────┐ │  │             │  │                      │
│ │Own Secrets:  │ │  │             │  │                      │
│ │- Kafka creds │ │  │             │  │                      │
│ │- Schema key  │ │  │             │  │                      │
│ │- Own KV      │ │  │             │  │                      │
│ └──────────────┘ │  │             │  │                      │
└──────────────────┘  └─────────────┘  └──────────────────────┘  
✅ **Both can produce:** Application A and B can both send encrypted messages  
✅ **Both can consume:** Application A and B can both receive and decrypt messages  
✅ **No secret sharing:** Application A cannot access Application B's secrets and vice versa

Both apps can produce AND consume on the same topic!
NO secrets are shared between Application A and Application B.
```

### Key Principles

✅ **Shared:** Schema Registry, Encryption KEK, Kafka Cluster  
✅ **Separate:** Each app's credentials, secrets, Key Vaults (for app secrets)  
✅ **Same rules:** Both apps use same encryption ruleset from Schema Registry  
✅ **Same KEK:** Both apps access same Key Encryption Key for field encryption

---

## Understanding CSFLE Components

### What is What: Keys vs Executors

**KEK (Key Encryption Key) = The Actual Encryption Key 🔑**
```bash
# The actual encryption key stored in Azure Key Vault
az keyvault key create \
  --vault-name shared-encryption-kv \
  --name customer-data-kek \
  --kty RSA \
  --size 2048
```

- **What it is:** The actual cryptographic key used for encryption/decryption
- **Where it lives:** Azure Key Vault (shared between both applications)
- **Who can access it:** Applications with proper Azure Key Vault permissions
- **Defined in:** Schema Registry ruleset (tells which KEK to use)

**FieldEncryptionExecutor = The Encryption Engine 🔧**
```csharp
var credential = new DefaultAzureCredential();
var fieldEncryptionExecutor = new AzureFieldEncryptionExecutor(credential);
```

- **What it is:** A component that performs encryption/decryption operations
- **What it does:** Calls Azure Key Vault to encrypt/decrypt data using the KEK
- **What it knows:** How to authenticate to Azure Key Vault and use the KEK
- **What it ISN'T:** The encryption key itself (it's just the engine that uses the key)

**DefaultAzureCredential = Authentication 🎫**
```csharp
var credential = new DefaultAzureCredential();
```

- **What it is:** Credentials to authenticate to Azure Key Vault
- **How it works:** Uses Managed Identity, Azure CLI, Visual Studio, etc.
- **What it does:** Proves the application is authorized to access the KEK

### How They Work Together

```
┌──────────────────────────────────────────────────────────────┐
│ Your Application (Producer or Consumer)                      │
│                                                               │
│  customer.Ssn = "123-45-6789" (plaintext in memory)         │
│         ↓                                                     │
│  AvroSerializer/Deserializer                                 │
│  ├─ Fetches schema from Schema Registry                     │
│  ├─ Reads ruleset: "encrypt PII using customer-data-kek"   │
│  └─ Calls FieldEncryptionExecutor                           │
│         ↓                                                     │
│  FieldEncryptionExecutor                                     │
│  ├─ Uses DefaultAzureCredential to authenticate             │
│  └─ Calls Azure Key Vault: "Encrypt using customer-data-kek"│
└──────────────────┬───────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────┐
│ Azure Key Vault (Shared)                                     │
│                                                               │
│  ┌────────────────────────────────────────────┐             │
│  │  KEK: customer-data-kek                    │             │
│  │  Type: RSA-2048                            │             │
│  │  Value: [actual key material - secret!]    │ ← The Key!  │
│  └────────────────────────────────────────────┘             │
│                                                               │
│  Encrypts "123-45-6789" using KEK                            │
│  Returns: <encrypted bytes>                                  │
└──────────────────┬───────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────┐
│ Kafka Message                                                 │
│  [Magic Byte][Schema ID][<encrypted bytes for SSN>][...]    │
└──────────────────────────────────────────────────────────────┘
```

### Encryption Flow Step-by-Step

**When Producing (Encryption):**

1. **Your code:** `customer.Ssn = "123-45-6789"`
2. **Serializer:** Fetches schema from Schema Registry
3. **Ruleset says:** "Field `ssn` has tag `PII`, encrypt using KEK `customer-data-kek`"
4. **FieldEncryptionExecutor:** Uses `DefaultAzureCredential` to authenticate to Azure Key Vault
5. **Azure Key Vault:** Encrypts `"123-45-6789"` using KEK `customer-data-kek`
6. **Result:** Encrypted bytes sent to Kafka

**When Consuming (Decryption):**

1. **Kafka:** Receives message with encrypted bytes
2. **Deserializer:** Fetches schema from Schema Registry
3. **Ruleset says:** "Field `ssn` is encrypted, decrypt using KEK `customer-data-kek`"
4. **FieldEncryptionExecutor:** Uses `DefaultAzureCredential` to authenticate to Azure Key Vault
5. **Azure Key Vault:** Decrypts encrypted bytes using KEK `customer-data-kek`
6. **Result:** `customer.Ssn = "123-45-6789"` (plaintext in memory)

### Key Concepts Summary

| Component | What It Is | Where It Lives | Shared? |
|-----------|------------|----------------|---------|
| **KEK (customer-data-kek)** | Actual encryption key | Azure Key Vault | ✅ Shared (both apps access same KEK) |
| **FieldEncryptionExecutor** | Encryption engine | Application code | ❌ Each app creates its own |
| **DefaultAzureCredential** | Authentication | Application code | ❌ Each app uses its own identity |
| **Ruleset** | Encryption rules | Schema Registry | ✅ Shared (defines which KEK to use) |
| **Avro Schema** | Data structure | Schema Registry | ✅ Shared (with PII tags) |

**Important:** The KEK is the ONLY encryption-related component that's shared. Each application has its own FieldEncryptionExecutor and credentials, but they both access the same KEK in Azure Key Vault.

---

## Producer and Consumer Flow Explained

### Producer Side (Application A sends message)

**What happens when you produce a message:**

```
1. Your code: producer.ProduceAsync(customer)
   customer.Ssn = "123-45-6789" (plaintext)
   ↓
2. AvroSerializer contacts Schema Registry
   - Fetches schema: "What fields exist? What are their types?"
   - Fetches ruleset: "Which fields to encrypt? Which KEK to use?"
   ↓
3. Ruleset says: "Field 'ssn' has tag 'PII' → encrypt using KEK 'customer-data-kek'"
   ↓
4. FieldEncryptionExecutor calls Azure Key Vault
   - Authenticates with App A's Managed Identity
   - Sends request: "Encrypt '123-45-6789' using key 'customer-data-kek'"
   ↓
5. Azure Key Vault performs the encryption
   - Uses the KEK (which never leaves the vault)
   - Returns encrypted bytes: <0xA7F3B2...>
   ↓
6. AvroSerializer puts encrypted bytes into the message
   - Replaces plaintext SSN with encrypted data
   ↓
7. Message sent to Kafka with encrypted data
   - Only encrypted bytes travel over the network
```

**Key points for producers:**
- ✅ Schema Registry provides **instructions** (which fields to encrypt, which KEK to use)
- ✅ Azure Key Vault performs the **actual encryption** (cryptographic operation)
- ✅ FieldEncryptionExecutor is the **bridge** (takes instructions, calls Azure)
- ✅ Encryption happens **inside Azure Key Vault**, not in your application
- ✅ Your app sends plaintext to Azure, receives encrypted bytes back
- ✅ The KEK never leaves Azure Key Vault

### Consumer Side (Application B reads message)

**What happens when you consume a message:**

```
1. Consumer receives message from Kafka (with encrypted bytes)
   - Message contains: <0xA7F3B2...> (encrypted SSN)
   ↓
2. AvroDeserializer contacts Schema Registry
   - Fetches schema: "What fields exist? What are their types?"
   - Fetches ruleset: "Which fields are encrypted? Which KEK was used?"
   ↓
3. Ruleset says: "Field 'ssn' is encrypted → decrypt using KEK 'customer-data-kek'"
   ↓
4. FieldEncryptionExecutor calls Azure Key Vault
   - Authenticates with App B's Managed Identity
   - Sends request: "Decrypt <0xA7F3B2...> using key 'customer-data-kek'"
   ↓
5. Azure Key Vault performs the decryption
   - Uses the KEK (same one App A used)
   - Returns plaintext: "123-45-6789"
   ↓
6. AvroDeserializer creates Customer object with decrypted data
   - customer.Ssn = "123-45-6789"
   ↓
7. Your code receives Customer with plaintext PII in memory
   - Now you can process the decrypted data
```

**Key points for consumers:**
- ✅ Uses **same Schema Registry** (gets same instructions as producer)
- ✅ Uses **same KEK** in Azure Key Vault (both apps use `customer-data-kek`)
- ✅ Uses **same ruleset** (knows which fields were encrypted)
- ✅ Uses **different credentials** (App B's Managed Identity, not App A's)
- ✅ Decryption happens **inside Azure Key Vault**, not in your application
- ✅ Your app sends encrypted bytes to Azure, receives plaintext back

### Both Applications Use the Same Azure Key Vault

```
┌─────────────────────────────────────────────────────────────┐
│ Application A (Producer)                                     │
│ - Has Managed Identity A                                    │
│ - Calls Azure Key Vault: "Encrypt using customer-data-kek" │
│ - Sends plaintext → receives encrypted bytes               │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
        ┌────────────────────────────────────┐
        │ SAME Azure Key Vault               │
        │ (shared-encryption-kv)             │
        │                                    │
        │  ┌──────────────────────────────┐  │
        │  │ KEK: customer-data-kek       │  │
        │  │ Type: RSA-2048               │  │
        │  │ - Encrypts for App A         │  │
        │  │ - Decrypts for App B         │  │
        │  └──────────────────────────────┘  │
        │                                    │
        │  Both apps access same KEK         │
        │  with different identities         │
        └────────────────────────────────────┘
                     ▲
                     │
┌────────────────────┴────────────────────────────────────────┐
│ Application B (Consumer)                                     │
│ - Has Managed Identity B                                    │
│ - Calls Azure Key Vault: "Decrypt using customer-data-kek" │
│ - Sends encrypted bytes → receives plaintext               │
└─────────────────────────────────────────────────────────────┘
```

### What's Shared vs What's Different

**SHARED Infrastructure (Both apps use the same):**
- ✅ Azure Key Vault (`shared-encryption-kv`)
- ✅ KEK (`customer-data-kek`) - THE KEY ITSELF
- ✅ Schema Registry (same schemas, same rulesets)
- ✅ Kafka cluster (same topics)

**SEPARATE per Application (Each app has its own):**
- ❌ Managed Identity (App A has Identity A, App B has Identity B)
- ❌ Kafka credentials (stored in App A's Key Vault vs App B's Key Vault)
- ❌ Schema Registry API keys (stored in App A's Key Vault vs App B's Key Vault)
- ❌ FieldEncryptionExecutor instance (each app creates its own in code)
- ❌ Application configuration (different appsettings.json files)

**Why this design?**
- **Security:** Applications can't access each other's Kafka credentials
- **Encryption:** Both applications can encrypt/decrypt using the same KEK
- **Isolation:** If one app is compromised, the other's credentials remain safe
- **Flexibility:** Each app can be deployed, scaled, and managed independently

---

## Secrets Separation Strategy

### What Each Application Keeps Secret

| Secret Type | Application A (Producer) | Application B (Consumer) | Shared? |
|------------|-------------------------|-------------------------|---------|
| Kafka Username/Password | ✅ A's credentials | ✅ B's credentials | ❌ Different |
| Schema Registry API Key | ✅ A's key (READ+WRITE) | ✅ B's key (READ only) | ❌ Different |
| Application Settings | ✅ A's Key Vault | ✅ B's Key Vault | ❌ Different |
| Managed Identity | ✅ A's Identity | ✅ B's Identity | ❌ Different |
| Encryption KEK | ✅ Both access same | ✅ Both access same | ✅ **SHARED** |
| Ruleset | ✅ Uses from registry | ✅ Uses from registry | ✅ **SHARED** |

### Architecture Pattern

```
Application A's Key Vault          Application B's Key Vault
├── kafka-username-a               ├── kafka-username-b
├── kafka-password-a               ├── kafka-password-b
├── schema-registry-key-a          ├── schema-registry-key-b
└── app-specific-secrets-a         └── app-specific-secrets-b

        ↓ Both access ↓

    Shared Key Vault (or same vault, different permissions)
    └── customer-kek (Encryption Key)
```

---

## Prerequisites

### Required NuGet Packages

```xml
<!-- Both Application A and B need these -->
<PackageReference Include="Confluent.Kafka" Version="2.9.0" />
<PackageReference Include="Confluent.SchemaRegistry" Version="2.9.0" />
<PackageReference Include="Confluent.SchemaRegistry.Serdes.Avro" Version="2.9.0" />

<!-- CSFLE encryption support -->
<PackageReference Include="Confluent.SchemaRegistry.Encryption.Azure" Version="2.9.0" />
<!-- Provides AzureFieldEncryptionExecutor -->

<!-- Azure Key Vault access -->
<PackageReference Include="Azure.Security.KeyVault.Keys" Version="4.7.0" />
<!-- Provides access to KEK in Azure Key Vault -->

<!-- Azure authentication -->
<PackageReference Include="Azure.Identity" Version="1.13.1" />
<!-- Provides DefaultAzureCredential for Managed Identity -->
```

### Infrastructure Requirements

- ✅ Kafka cluster (localhost or cloud)
- ✅ Schema Registry (Confluent or compatible)
- ✅ Azure Key Vault (for KEK storage)
- ✅ Two Azure Managed Identities (one per app)

---

## Schema Registry Setup

### Step 1: Define Avro Schema with PII Tags

Create `Customer.avsc`:

```json
{
  "type": "record",
  "name": "Customer",
  "namespace": "MyCompany.Models",
  "fields": [
    {
      "name": "id",
      "type": "long"
    },
    {
      "name": "name",
      "type": "string"
    },
    {
      "name": "email",
      "type": "string"
    },
    {
      "name": "ssn",
      "type": "string",
      "confluent:tags": ["PII"]
    },
    {
      "name": "creditCardNumber",
      "type": "string",
      "confluent:tags": ["PII"]
    },
    {
      "name": "address",
      "type": "string"
    },
    {
      "name": "createdAt",
      "type": "long",
      "logicalType": "timestamp-millis"
    }
  ]
}
```

### Step 2: Register Schema with Encryption Ruleset

```bash
# Register the schema with encryption ruleset
# This is typically done once by a platform/data team

curl -X POST http://schema-registry:8081/subjects/customer-events-value/versions \
  -H "Content-Type: application/json" \
  -d '{
    "schemaType": "AVRO",
    "schema": "{\"type\":\"record\",\"name\":\"Customer\",\"namespace\":\"MyCompany.Models\",\"fields\":[{\"name\":\"id\",\"type\":\"long\"},{\"name\":\"name\",\"type\":\"string\"},{\"name\":\"email\",\"type\":\"string\"},{\"name\":\"ssn\",\"type\":\"string\",\"confluent:tags\":[\"PII\"]},{\"name\":\"creditCardNumber\",\"type\":\"string\",\"confluent:tags\":[\"PII\"]},{\"name\":\"address\",\"type\":\"string\"},{\"name\":\"createdAt\",\"type\":\"long\",\"logicalType\":\"timestamp-millis\"}]}",
    "ruleSet": {
      "domainRules": [
        {
          "name": "encryptPII",
          "kind": "TRANSFORM",
          "type": "ENCRYPT",
          "mode": "WRITEREAD",
          "tags": ["PII"],
          "params": {
            "encrypt.kek.name": "customer-data-kek",
            "encrypt.kms.type": "azure-kms",
            "encrypt.kms.key.id": "https://shared-kv.vault.azure.net/keys/customer-data-kek"
          },
          "onFailure": "ERROR"
        }
      ]
    }
  }'
```

### Step 3: Verify Registration

```bash
# Check the schema was registered
curl http://schema-registry:8081/subjects/customer-events-value/versions/latest

# Response should show the ruleset
{
  "subject": "customer-events-value",
  "version": 1,
  "id": 1,
  "schema": "...",
  "ruleSet": {
    "domainRules": [...]
  }
}
```

---

## Azure Key Vault Setup

### Step 1: Create Shared Key Vault for KEK

**IMPORTANT:** This is the actual encryption key (KEK) that will be used for CSFLE. Both Application A and Application B will access this same key.

```bash
# Create shared Key Vault for encryption keys
az keyvault create \
  --name shared-encryption-kv \
  --resource-group shared-resources-rg \
  --location eastus

# Create the Key Encryption Key (KEK) - This is the ACTUAL encryption key!
az keyvault key create \
  --vault-name shared-encryption-kv \
  --name customer-data-kek \
  --kty RSA \
  --size 2048 \
  --ops encrypt decrypt wrapKey unwrapKey

# Get the key ID (you'll need this for the ruleset)
az keyvault key show \
  --vault-name shared-encryption-kv \
  --name customer-data-kek \
  --query key.kid -o tsv
# Output: https://shared-encryption-kv.vault.azure.net/keys/customer-data-kek/xxx
```

### Step 2: Create Application-Specific Key Vaults

```bash
# Application A's Key Vault (for app secrets)
az keyvault create \
  --name app-a-secrets-kv \
  --resource-group app-a-rg \
  --location eastus

# Application B's Key Vault (for app secrets)
az keyvault create \
  --name app-b-secrets-kv \
  --resource-group app-b-rg \
  --location eastus
```

### Step 3: Store Application-Specific Secrets

```bash
# Application A secrets
az keyvault secret set \
  --vault-name app-a-secrets-kv \
  --name kafka-username \
  --value "producer-app-user"

az keyvault secret set \
  --vault-name app-a-secrets-kv \
  --name kafka-password \
  --value "producer-app-password"

az keyvault secret set \
  --vault-name app-a-secrets-kv \
  --name schema-registry-api-key \
  --value "producer-schema-key"

az keyvault secret set \
  --vault-name app-a-secrets-kv \
  --name schema-registry-api-secret \
  --value "producer-schema-secret"

# Application B secrets
az keyvault secret set \
  --vault-name app-b-secrets-kv \
  --name kafka-username \
  --value "consumer-app-user"

az keyvault secret set \
  --vault-name app-b-secrets-kv \
  --name kafka-password \
  --value "consumer-app-password"

az keyvault secret set \
  --vault-name app-b-secrets-kv \
  --name schema-registry-api-key \
  --value "consumer-schema-key"

az keyvault secret set \
  --vault-name app-b-secrets-kv \
  --name schema-registry-api-secret \
  --value "consumer-schema-secret"
```

### Step 4: Configure Managed Identities

```bash
# Enable Managed Identity for Application A
az webapp identity assign \
  --name app-a-producer \
  --resource-group app-a-rg

# Get Application A's Managed Identity Object ID
APP_A_IDENTITY=$(az webapp identity show \
  --name app-a-producer \
  --resource-group app-a-rg \
  --query principalId -o tsv)

# Enable Managed Identity for Application B
az webapp identity assign \
  --name app-b-consumer \
  --resource-group app-b-rg

# Get Application B's Managed Identity Object ID
APP_B_IDENTITY=$(az webapp identity show \
  --name app-b-consumer \
  --resource-group app-b-rg \
  --query principalId -o tsv)
```

### Step 5: Grant Permissions

```bash
# Application A: Access to its own secrets
az keyvault set-policy \
  --name app-a-secrets-kv \
  --object-id $APP_A_IDENTITY \
  --secret-permissions get list

# Application A: Access to shared KEK
az keyvault set-policy \
  --name shared-encryption-kv \
  --object-id $APP_A_IDENTITY \
  --key-permissions get wrapKey unwrapKey encrypt decrypt

# Application B: Access to its own secrets
az keyvault set-policy \
  --name app-b-secrets-kv \
  --object-id $APP_B_IDENTITY \
  --secret-permissions get list

# Application B: Access to shared KEK
az keyvault set-policy \
  --name shared-encryption-kv \
  --object-id $APP_B_IDENTITY \
  --key-permissions get wrapKey unwrapKey encrypt decrypt
```
Full Implementation (Producer + Consumer)

**Note:** Application A has BOTH producer and consumer capabilities. It can send encrypted messages AND receive/decrypt messages from Application B (or itself).

### Directory Structure

```
ApplicationA/
├── ApplicationA.csproj
├── Program.cs
├── Models/
│   └── Customer.cs
├── Services/
│   ├── KafkaProducerService.cs
│   ├── KafkaConsumerService.cs
│   └── CustomerProcessor
│   └── Customer.cs
├── Services/
│   └── KafkaProducerService.cs
├── appsettings.json
└── appsettings.Production.json
```

### Models/Customer.cs

```csharp
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace ApplicationA.Models;

[Schema(@"{
  ""type"": ""record"",
  ""name"": ""Customer"",
  ""namespace"": ""MyCompany.Models"",
  ""fields"": [
    { ""name"": ""id"", ""type"": ""long"" },
    { ""name"": ""name"", ""type"": ""string"" },
    { ""name"": ""email"", ""type"": ""string"" },
    { ""name"": ""ssn"", ""type"": ""string"", ""confluent:tags"": [""PII""] },
    { ""name"": ""creditCardNumber"", ""type"": ""string"", ""confluent:tags"": [""PII""] },
    { ""name"": ""address"", ""type"": ""string"" },
    { ""name"": ""createdAt"", ""type"": ""long"", ""logicalType"": ""timestamp-millis"" }
  ]
}")]
public class Customer
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Ssn { get; set; } = string.Empty;
    public string CreditCardNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
}
```

### Services/KafkaProducerService.cs

```csharp
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Confluent.SchemaRegistry.Encryption;
using Confluent.SchemaRegistry.Encryption.Azure;
using Azure.Identity;
using ApplicationA.Models;

namespace ApplicationA.Services;

public interface IKafkaProducerService
{
    Task<DeliveryResult<string, Customer>> ProduceCustomerAsync(Customer customer);
}

public class KafkaProducerService : IKafkaProducerService, IDisposable
{
    private readonly IProducer<string, Customer> _producer;
    private readonly ILogger<KafkaProducerService> _logger;
    private readonly string _topicName;

    public KafkaProducerService(
        IConfiguration configuration,
        ILogger<KafkaProducerService> logger)
    {
        _logger = logger;
        _topicName = configuration["Kafka:TopicName"] ?? "customer-events";

        // Schema Registry configuration
        var schemaRegistryConfig = new SchemaRegistryConfig
        {
            Url = configuration["SchemaRegistry:Url"],
            BasicAuthUserInfo = $"{configuration["SchemaRegistry:ApiKey"]}:{configuration["SchemaRegistry:ApiSecret"]}"
        };

        var schemaRegistry = new CachedSchemaRegistryClient(schemaRegistryConfig);

        // Azure Field Encryption Executor (for CSFLE)
        // This is the ENCRYPTION ENGINE, not the key!
        // It uses DefaultAzureCredential to authenticate to Azure Key Vault
        // and access the KEK (customer-data-kek) defined in the ruleset
        var credential = new DefaultAzureCredential();
        var fieldEncryptionExecutor = new AzureFieldEncryptionExecutor(credential);

        // Avro Serializer with field encryption
        var avroSerializerConfig = new AvroSerializerConfig
        {
            AutoRegisterSchemas = false,  // Schema already registered with ruleset
            UseLatestVersion = true
        };

        // Kafka Producer configuration
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"],
            ClientId = "application-a-producer",
            Acks = Acks.Leader,
            EnableIdempotence = true,
            CompressionType = CompressionType.Snappy,
            
            // Security (if using SASL/SSL)
            SecurityProtocol = SecurityProtocol.SaslSsl,
            SaslMechanism = SaslMechanism.Plain,
            SaslUsername = configuration["Kafka:Username"],
            SaslPassword = configuration["Kafka:Password"]
        };

        _producer = new ProducerBuilder<string, Customer>(producerConfig)
            .SetValueSerializer(new AvroSerializer<Customer>(
                schemaRegistry,
                avroSerializerConfig,
                fieldEncryptionExecutor))  // CSFLE encryption
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka error: Code={Code}, Reason={Reason}", 
                    error.Code, error.Reason);
            })
            .Build();

        _logger.LogInformation("Kafka Producer initialized for topic: {Topic}", _topicName);
    }

    public async Task<DeliveryResult<string, Customer>> ProduceCustomerAsync(Customer customer)
    {
        try
        {
            _logger.LogInformation("Producing customer: {CustomerId}", customer.Id);

            var result = await _producer.ProduceAsync(_topicName, new Message<string, Customer>
            {
                Key = customer.Id.ToString(),
                Value = customer,  // SSN and CreditCard will be encrypted automatically
                Headers = new Headers
                {
                    { "source", System.Text.Encoding.UTF8.GetBytes("application-a") },
                    { "timestamp", System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("o")) }
                }
            });

            _logger.LogInformation(
                "Message produced: Topic={Topic}, Partition={Partition}, Offset={Offset}",
                result.Topic, result.Partition.Value, result.Offset.Value);

            return result;
        }
        catch (ProduceException<string, Customer> ex)
        {
            _logger.LogError(ex, "Failed to produce message: {Error}", ex.Error.Reason);
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(10));
        _producer?.Dispose();
    }
}
```

### appsettings.json (Application A)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "TopicName": "customer-events",
    "Username": "",
    "Password": ""
  },
  "SchemaRegistry": {
    "Url": "http://localhost:8081",
    "ApiKey": "",
    "ApiSecret": ""
  },
  "AzureKeyVault": {
    "AppSecretsVaultUrl": "https://app-a-secrets-kv.vault.azure.net/",
    "SharedEncryptionVaultUrl": "https://shared-encryption-kv.vault.azure.net/"
  }
}
```

### appsettings.Production.json (Application A)

```json
{
  "Kafka": {
    "BootstrapServers": "@Microsoft.KeyVault(SecretUri=https://app-a-secrets-kv.vault.azure.net/secrets/kafka-bootstrap-servers)",
    "Username": "@Microsoft.KeyVault(SecretUri=https://app-a-secrets-kv.vault.azure.net/secrets/kafka-username)",
    "Password": "@Microsoft.KeyVault(SecretUri=https://app-a-secrets-kv.vault.azure.net/secrets/kafka-password)"
  },
  "SchemaRegistry": {
    Services/KafkaConsumerService.cs (Application A)

```csharp
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Confluent.SchemaRegistry.Encryption;
using Confluent.SchemaRegistry.Encryption.Azure;
using Azure.Identity;
using ApplicationA.Models;

namespace ApplicationA.Services;

public class KafkaConsumerService : BackgroundService
{
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly IConsumer<string, Customer> _consumer;
    private readonly ICustomerProcessor _customerProcessor;
    private readonly string _topicName;

    public KafkaConsumerService(
        IConfiguration configuration,
        ILogger<KafkaConsumerService> logger,
        ICustomerProcessor customerProcessor)
    {
        _logger = logger;
        _customerProcessor = customerProcessor;
        _topicName = configuration["Kafka:TopicName"] ?? "customer-events";

        // Schema Registry configuration (Application A's own credentials)
        var schemaRegistryConfig = new SchemaRegistryConfig
        {
            Url = configuration["SchemaRegistry:Url"],
            BasicAuthUserInfo = $"{configuration["SchemaRegistry:ApiKey"]}:{configuration["SchemaRegistry:ApiSecret"]}"
        };

        var schemaRegistry = new CachedSchemaRegistryClient(schemaRegistryConfig);

        // Azure Field Encryption Executor (using Application A's Managed Identity)
        var credential = new DefaultAzureCredential();
        var fieldEncryptionExecutor = new AzureFieldEncryptionExecutor(credential);

        var avroDeserializerConfig = new AvroDeserializerConfig();

        // Kafka Consumer configuration (Application A's own Kafka credentials)
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"],
            GroupId = "application-a-consumer-group",  // App A's own consumer group
            ClientId = "application-a-consumer",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            
            SecurityProtocol = SecurityProtocol.SaslSsl,
            SaslMechanism = SaslMechanism.Plain,
            SaslUsername = configuration["Kafka:Username"],  // App A's Kafka user
            SaslPassword = configuration["Kafka:Password"]   // App A's Kafka password
        };

        _consumer = new ConsumerBuilder<string, Customer>(consumerConfig)
            .SetValueDeserializer(new AvroDeserializer<Customer>(
                schemaRegistry,
                avroDeserializerConfig,
                fieldEncryptionExecutor))
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Application A consumer error: Code={Code}, Reason={Reason}",
                    error.Code, error.Reason);
            })
            .Build();

        _logger.LogInformation("Application A Kafka Consumer initialized for topic: {Topic}", _topicName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe(_topicName);
        _logger.LogInformation("Application A subscribed to topic: {Topic}", _topicName);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {Full Implementation (Producer + Consumer)

**Note:** Application B ALSO has BOTH producer and consumer capabilities. It can send encrypted messages AND receive/decrypt messages from Application A (or itself). Application B uses completely different credentials than Application A.

### Directory Structure

```
ApplicationB/
├── ApplicationB.csproj
├── Program.cs
├── Models/
│   └── Customer.cs
├── Services/
│   ├── KafkaProducerService.cs               consumeResult.Partition.Value,
                            consumeResult.Offset.Value);

                        // Process the customer (automatically decrypted)
                        await _customerProcessor.ProcessCustomerAsync(consumeResult.Message.Value);

                        _consumer.Commit(consumeResult);
                    })

```csharp
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace ApplicationB.Models;

// IMPORTANT: Same schema as Application A - this is how they communicate!
// Both apps use the same schema from Schema Registry
        {
            _consumer.Close();
        }
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}
```

### Services/CustomerProcessor.cs (Application A)

```csharp
using ApplicationA.Models;

namespace ApplicationA.Services;

public interface ICustomerProcessor
{
    Task ProcessCustomerAsync(Customer customer);
}

public class CustoProducerService.cs (Application B)

```csharp
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Confluent.SchemaRegistry.Encryption;
using Confluent.SchemaRegistry.Encryption.Azure;
using Azure.Identity;
using ApplicationB.Models;

namespace ApplicationB.Services;

public interface IKafkaProducerService
{
    Task<DeliveryResult<string, Customer>> ProduceCustomerAsync(Customer customer);
}

public class KafkaProducerService : IKafkaProducerService, IDisposable
{
    private readonly IProducer<string, Customer> _producer;
    private readonly ILogger<KafkaProducerService> _logger;
    private readonly string _topicName;

    public KafkaProducerService(
        IConfiguration configuration,
        ILogger<KafkaProducerService> logger)
    {
        _logger = logger;
        _topicName = configuration["Kafka:TopicName"] ?? "customer-events";

        // Schema Registry configuration (Application B's own credentials)
        var schemaRegistryConfig = new SchemaRegistryConfig
        {
            Url = configuration["SchemaRegistry:Url"],
            BasicAuthUserInfo = $"{configuration["SchemaRegistry:ApiKey"]}:{configuration["SchemaRegistry:ApiSecret"]}"
        };

        var schemaRegistry = new CachedSchemaRegistryClient(schemaRegistryConfig);

        // Azure Field Encryption Executor (using Application B's Managed Identity)
        var credential = new DefaultAzureCredential();
        var fieldEncryptionExecutor = new AzureFieldEncryptionExecutor(credential);

        var avroSerializerConfig = new AvroSerializerConfig
        {
            AutoRegisterSchemas = false,
            UseLatestVersion = true
        };

        // Kafka Producer configuration (Application B's own Kafka credentials)
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"],
            ClientId = "application-b-producer",  // Different client ID
            Acks = Acks.Leader,
            EnableIdempotence = true,
            CompressionType = CompressionType.Snappy,
            
            SecurityProtocol = SecurityProtocol.SaslSsl,
            SaslMechanism = SaslMechanism.Plain,
            SaslUsername = configuration["Kafka:Username"],  // App B's Kafka user
            SaslPassword = configuration["Kafka:Password"]   // App B's Kafka password
        };

        _producer = new ProducerBuilder<string, Customer>(producerConfig)
            .SetValueSerializer(new AvroSerializer<Customer>(
                schemaRegistry,
                avroSerializerConfig,
                fieldEncryptionExecutor))
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Application B producer error: Code={Code}, Reason={Reason}", 
                    error.Code, error.Reason);
            })
            .Build();

        _logger.LogInformation("Application B Kafka Producer initialized for topic: {Topic}", _topicName);
    }

    public async Task<DeliveryResult<string, Customer>> ProduceCustomerAsync(Customer customer)
    {
        try
        {
            _logger.LogInformation("Application B producing customer: {CustomerId}", customer.Id);

            var result = await _producer.ProduceAsync(_topicName, new Message<string, Customer>
            {
                Key = customer.Id.ToString(),
                Value = customer,  // PII encrypted automatically
                Headers = new Headers
                {
                    { "source", System.Text.Encoding.UTF8.GetBytes("application-b") },
                    { "timestamp", System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("o")) }
                }
            });

            _logger.LogInformation(
                "Application B message produced: Topic={Topic}, Partition={Partition}, Offset={Offset}",
                result.Topic, result.Partition.Value, result.Offset.Value);

            return result;
        }
        catch (ProduceException<string, Customer> ex)
        {
            _logger.LogError(ex, "Failed to produce message: {Error}", ex.Error.Reason);
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(10));
        _producer?.Dispose();
    }
}
```

### Services/KafkaConsumerService.cs (Application B)

```csharp
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Confluent.SchemaRegistry.Encryption;
using Confluent.SchemaRegistry.Encryption.Azure;
using Azure.Identity;
using ApplicationB.Models;

namespace ApplicationB.Services; (Application B)

```csharp
using ApplicationB.Models;

namespace ApplicationB.Services;

public interface ICustomerProcessor
{
    Task ProcessCustomerAsync(Customer customer);
}

public class CustomerProcessor : ICustomerProcessor
{
    private readonly ILogger<CustomerProcessor> _logger;

    public CustomerProcessor(ILogger<CustomerProcessor> logger)
    {
        _logger = logger;
    }

    public async Task ProcessCustomerAsync(Customer customer)
    {
        _logger.LogInformation(
            "Application B processing customer: Id={Id}, Name={Name}",
            customer.Id, customer.Name);

        // PII fields are decrypted here
        _logger.LogInformation("Application B received decrypted data successfully");

        // Application B's business logic (different from Application A)
        await Task.CompletedTask;
    }
}
```

### API Controller (Application B)

```csharp
using Microsoft.AspNetCore.Mvc;
using ApplicationB.Services;
using ApplicationB.Models;

namespace ApplicationB.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly IKafkaProducerService _producerService;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(
        IKafkaProducerService producerService,
        ILogger<CustomersController> logger)
    {
        _producerService = producerService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateCustomer([FromBody] Customer customer)
    {
        try
        {
            customer.Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            customer.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Application B can also produce messages!
            var result = await _producerService.ProduceCustomerAsync(customer);

            return Ok(new
            {
                success = true,
                customerId = customer.Id,
                partition = result.Partition.Value,
                offset = result.Offset.Value,
                message = "Application B sent encrypted message",
                source = "application-b"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Application B failed to create customer");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
                _logger.LogError("Application B consumer error: Code={Code}, Reason={Reason}",
                    error.Code, error.Reason);
            })
            .Build();

        _logger.LogInformation("Application B Kafka Consumer initialized for topic: {Topic}", _topicName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe(_topicName);
        _logger.LogInformation("Application B subscribed to topic: {Topic}", _topicName);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);

                    if (consumeResult?.Message?.Value != null)
                    {
                        _logger.LogInformation(
                            "Application B consumed message: Key={Key}, Partition={Partition}, Offset={Offset}",
                            consumeResult.Message.Key,
                            consumeResult.Partition.Value,
                            consumeR (Application B's own Key Vault)
if (builder.Environment.IsProduction())
{
    var keyVaultUrl = builder.Configuration["AzureKeyVault:AppSecretsVaultUrl"];
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl!),
        new DefaultAzureCredential());
}

// Add services
builder.Services.AddControllers();

// Application B ALSO has BOTH producer and consumer
builder.Services.AddSingleton<IKafkaProducerService, KafkaProducerService>();
builder.Services.AddSingleton<ICustomerProcessor, CustomerProcessor>();
builder.Services.AddHostedService<KafkaConsumerService>();

var app = builder.Build();

app.MapControllers();        finally
        {
            _consumer.Close(

### Directory Structure

```
ApplicationB/
├── ApplicationB.csproj
├── Program.cs
├── Models/
│   └── Customer.cs
├── Services/
│   ├── KafkaConsumerService.cs
│   └── CustomerProcessor.cs
├── appsettings.json
└── appsettings.Production.json
```

### Models/Customer.cs (Application B - Same schema)

```csharpBoth Applications

```bash
# Terminal 1: Start Application A (has both producer and consumer)
cd ApplicationA
dotnet run --urls "http://localhost:5000"

# Should see:
# info: Application A Kafka Producer initialized for topic: customer-events
# info: Application A Kafka Consumer initialized for topic: customer-events
# info: Application A subscribed to topic: customer-events

# Terminal 2: Start Application B (has both producer and consumer)
cd ApplicationB
dotnet run --urls "http://localhost:5001"

# Should see:
# info: Application B Kafka Producer initialized for topic: customer-events
# info: Application B Kafka Consumer initialized for topic: customer-events
# info: Application B subscribed to topic: customer-events
```

### Step 4: Send Message from Application A

```bash
# Application A sends encrypted message
curl -X POST http://localhost:5000/api/customers \
  -H "Content-Type: application/json" \
  -d '{
    "name": "John Doe",
    "email": "john.doe@example.com",
    "ssn": "123-45-6789",
    "creditCardNumber": "4111-1111-1111-1111",
    "address": "123 Main St, New York, NY 10001"
  }'

# Response from Application A:
{
  "success": true,
  "customerId": 1738454400000,
  "partition": 0,
  "offset": 0,
  "message": "Customer data sent with encrypted PII fields"
}
```

### Step 5: Verify Both Applications Receive the Message

**Application A Logs (receives its own message):**
```
info: Application A produced message: Topic=customer-events, Partition=0, Offset=0
info: Application A consumed message: Key=1738454400000, Partition=0, Offset=0
info: Application A processing customer: Id=1738454400000, Name=John Doe
```

**Application B Logs (receives message from Application A):**
```
info: Application B consumed message: Key=1738454400000, Partition=0, Offset=0
info: Application B processing customer: Id=1738454400000, Name=John Doe
info: Application B received decrypted data successfully
```

### Step 6: Send Message from Application B

```bash
# Application B can ALSO send messages!
curl -X POST http://localhost:5001/api/customers \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Jane Smith",
    "email": "jane.smith@example.com",
    "ssn": "987-65-4321",
    "creditCardNumber": "5555-5555-5555-4444",
    "address": "456 Oak Ave, Boston, MA 02101"
  }'

# Response from Application B:
{
  "success": true,
  "customerId": 1738454401000,
  "partition": 0,
  "offset": 1,
  "message": "Application B sent encrypted message",
  "source": "application-b"
}
```

### Step 7: Verify Both Applications Receive Application B's Message

**Application A Logs (receives message from Application B):**
```
info: Application A consumed message: Key=1738454401000, Partition=0, Offset=1
info: Application A processing customer: Id=1738454401000, Name=Jane Smith
info: Application A received decrypted data successfully
```

**Application B Logs (receives its own message):**
```
info: Application B produced message: Topic=customer-events, Partition=0, Offset=1
info: Application B consumed message: Key=1738454401000, Partition=0, Offset=1
info: Application B processing customer: Id=1738454401000, Name=Jane Smith
public class KafkaConsumerService : BackgroundService
{
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly IConsumer<string, Customer> _consumer;
    private readonly ICustomerProcessor _customerProcessor;
    private readonly string _topicName;

    public KafkaConsumerService(
        IConfiguration configuration,
        ILogger<KafkaConsumerService> logger,
        ICustomerProcessor customerProcessor)
    {
        _logger = logger;
        _customerProcessor = customerProcessor;
        _topicName = configuration["Kafka:TopicName"] ?? "customer-events";

        // Schema Registry configuration
        var schemaRegistryConfig = new SchemaRegistryConfig
        {
            Url = configuration["SchemaRegistry:Url"],
            BasicAuthUserInfo = $"{configuration["SchemaRegistry:ApiKey"]}:{configuration["SchemaRegistry:ApiSecret"]}"
        };

        var schemaRegistry = new CachedSchemaRegistryClient(schemaRegistryConfig);

        // Azure Field Encryption Executor (for CSFLE decryption)
        var credential = new DefaultAzureCredential();
        var fieldEncryptionExecutor = new AzureFieldEncryptionExecutor(credential);

        // Avro Deserializer with field decryption
        var avroDeserializerConfig = new AvroDeserializerConfig();

        // Kafka Consumer configuration
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"],
            GroupId = "application-b-consumer-group",
            ClientId = "application-b-consumer",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            
            // Security (if using SASL/SSL)
            SecurityProtocol = SecurityProtocol.SaslSsl,
            SaslMechanism = SaslMechanism.Plain,
            SaslUsername = configuration["Kafka:Username"],
            SaslPassword = configuration["Kafka:Password"]
        };

        _consumer = new ConsumerBuilder<string, Customer>(consumerConfig)
            .SetValueDeserializer(new AvroDeserializer<Customer>(
                schemaRegistry,
                avroDeserializerConfig,
                fieldEncryptionExecutor))  // CSFLE decryption
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka consumer error: Code={Code}, Reason={Reason}",
                    error.Code, error.Reason);
            })
            .Build();

        _logger.LogInformation("Kafka Consumer initialized for topic: {Topic}", _topicName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe(_topicName);
        _logger.LogInformation("Subscribed to topic: {Topic}", _topicName);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);

                    if (consumeResult?.Message?.Value != null)
                    {
                        _logger.LogInformation(
                            "Consumed message: Key={Key}, Partition={Partition}, Offset={Offset}",
                            consumeResult.Message.Key,
                            consumeResult.Partition.Value,
                            consumeResult.Offset.Value);

                        // Process the customer (PII fields automatically decrypted)
                        await _customerProcessor.ProcessCustomerAsync(consumeResult.Message.Value);

                        // Commit offset
                        _consumer.Commit(consumeResult);
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Error consuming message: {Error}", ex.Error.Reason);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message");
                }
            }
        }
        finally
        {
            _consumer.Close();
            _logger.LogInformation("Kafka consumer closed");
        }
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}
``` (shared-encryption-kv)
- ✅ Schema definition with ruleset
- ✅ Same Kafka topic (customer-events)

**What's Separate (NO SHARING!):**
- ❌ Application A's Kafka credentials (username/password)
- ❌ Application B's Kafka credentials (username/password)
- ❌ Application A's Schema Registry API key
- ❌ Application B's Schema Registry API key
- ❌ Application A's Key Vault (app-a-secrets-kv)
- ❌ Application B's Key Vault (app-b-secrets-kv)
- ❌ Application A's Managed Identity
- ❌ Application B's Managed Identity
- ❌ Application A's consumer group
- ❌ Application B's consumer group

**Capabilities:**
- ✅ Application A can **produce** encrypted messages
- ✅ Application A can **consume** encrypted messages (from itself or Application B)
- ✅ Application B can **produce** encrypted messages
- ✅ Application B can **consume** encrypted messages (from itself or Application A)
- ✅ Both use the **same encryption ruleset** from Schema Registry
- ✅ Both can **encrypt/decrypt** using the shared KEK
- ❌ Neither can access the other's secrets/credentials

**Security Model:**
- Each app has its own credentials stored in its own Key Vault
- Each app authenticates independently with its own Managed Identity
- Both apps can execute the same encryption ruleset (from Schema Registry)
- Neither app can access the other's secrets
- Both apps can encrypt/decrypt using the shared KEK (for data encryption only)
- Application A cannot read Application B's configuration or secrets
- Application B cannot read Application A's configuration or secrets

**Communication Flow:**
1. Application A produces encrypted message → Kafka topic
2. Application A consumes and decrypts the message (own consumer group)
3. Application B consumes and decrypts the message (own consumer group)
4. Application B produces encrypted message → Same Kafka topic
5. Application B consumes and decrypts the message (own consumer group)
6. Application A consumes and decrypts the message (own consumer group)

This architecture provides **complete secret isolation** while enabling **bidirectional
    }

    public async Task ProcessCustomerAsync(Customer customer)
    {
        // At this point, PII fields are already decrypted!
        _logger.LogInformation(
            "Processing customer: Id={Id}, Name={Name}, Email={Email}",
            customer.Id, customer.Name, customer.Email);

        // SSN and CreditCard are decrypted - handle with care!
        _logger.LogInformation("SSN (decrypted): {SSN}", MaskSsn(customer.Ssn));
        _logger.LogInformation("Credit Card (decrypted): {Card}", MaskCreditCard(customer.CreditCardNumber));

        // Your business logic here
        // - Save to database
        // - Send notifications
        // - Trigger workflows
        // etc.

        await Task.CompletedTask;
    }

    private string MaskSsn(string ssn)
    {
        if (string.IsNullOrEmpty(ssn) || ssn.Length < 4)
            return "***";
        
        return "***-**-" + ssn.Substring(ssn.Length - 4);
    }

    private string MaskCreditCard(string card)
    {
        if (string.IsNullOrEmpty(card) || card.Length < 4)
            return "****";
        
        return "****-****-****-" + card.Substring(card.Length - 4);
    }
}
```

### appsettings.json (Application B)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "TopicName": "customer-events",
    "Username": "",
    "Password": ""
  },
  "SchemaRegistry": {
    "Url": "http://localhost:8081",
    "ApiKey": "",
    "ApiSecret": ""
  },
  "AzureKeyVault": {
    "AppSecretsVaultUrl": "https://app-b-secrets-kv.vault.azure.net/",
    "SharedEncryptionVaultUrl": "https://shared-encryption-kv.vault.azure.net/"
  }
}
```

### appsettings.Production.json (Application B)

```json
{
  "Kafka": {
    "BootstrapServers": "@Microsoft.KeyVault(SecretUri=https://app-b-secrets-kv.vault.azure.net/secrets/kafka-bootstrap-servers)",
    "Username": "@Microsoft.KeyVault(SecretUri=https://app-b-secrets-kv.vault.azure.net/secrets/kafka-username)",
    "Password": "@Microsoft.KeyVault(SecretUri=https://app-b-secrets-kv.vault.azure.net/secrets/kafka-password)"
  },
  "SchemaRegistry": {
    "Url": "@Microsoft.KeyVault(SecretUri=https://app-b-secrets-kv.vault.azure.net/secrets/schema-registry-url)",
    "ApiKey": "@Microsoft.KeyVault(SecretUri=https://app-b-secrets-kv.vault.azure.net/secrets/schema-registry-api-key)",
    "ApiSecret": "@Microsoft.KeyVault(SecretUri=https://app-b-secrets-kv.vault.azure.net/secrets/schema-registry-api-secret)"
  }
}
```

### Program.cs (Application B)

```csharp
using ApplicationB.Services;
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Add Azure Key Vault configuration
if (builder.Environment.IsProduction())
{
    var keyVaultUrl = builder.Configuration["AzureKeyVault:AppSecretsVaultUrl"];
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl!),
        new DefaultAzureCredential());
}

// Add services
builder.Services.AddSingleton<ICustomerProcessor, CustomerProcessor>();
builder.Services.AddHostedService<KafkaConsumerService>();

var app = builder.Build();

app.Run();
```

---

## Testing the Flow

### Step 1: Start Infrastructure

```bash
# Start Kafka, Zookeeper, and Schema Registry with Docker Compose
docker-compose up -d
```

### Step 2: Verify Schema Registration

```bash
# Check the schema is registered with ruleset
curl http://localhost:8081/subjects/customer-events-value/versions/latest | jq
```

### Step 3: Start Application B (Consumer)

```bash
cd ApplicationB
dotnet run

# Should see:
# info: Kafka Consumer initialized for topic: customer-events
# info: Subscribed to topic: customer-events
```

### Step 4: Send Test Message from Application A

```bash
# Start Application A
cd ApplicationA
dotnet run

# Send test request
curl -X POST http://localhost:5000/api/customers \
  -H "Content-Type: application/json" \
  -d '{
    "name": "John Doe",
    "email": "john.doe@example.com",
    "ssn": "123-45-6789",
    "creditCardNumber": "4111-1111-1111-1111",
    "address": "123 Main St, New York, NY 10001"
  }'

# Response:
{
  "success": true,
  "customerId": 1738454400000,
  "partition": 0,
  "offset": 0,
  "message": "Customer data sent with encrypted PII fields"
}
```

### Step 5: Verify in Application B Logs

```
info: Consumed message: Key=1738454400000, Partition=0, Offset=0
info: Processing customer: Id=1738454400000, Name=John Doe, Email=john.doe@example.com
info: SSN (decrypted): ***-**-6789
info: Credit Card (decrypted): ****-****-****-1111
```

### Step 6: Inspect Message in Kafka (Raw)

```bash
# View raw message (encrypted)
kafka-console-consumer \
  --bootstrap-server localhost:90 - Detailed

```
1. Your Code
   customer.Ssn = "123-45-6789" (plaintext in memory)
   ↓
2. producer.ProduceAsync(customer)
   ↓
3. AvroSerializer
   ├─ Fetches schema from Schema Registry
   │  GET /subjects/customer-events-value/versions/latest
   ├─ Receives schema with ruleset:
   │  {
   │    "fields": [{"name": "ssn", "confluent:tags": ["PII"]}],
   │    "ruleSet": {
   │      "domainRules": [{
   │        "type": "ENCRYPT",
   │        "tags": ["PII"],
   │        "params": {
   │          "encrypt.kek.name": "customer-data-kek"
   │        }
   │      }]
   │    }
   │  }
   └─ Identifies field "ssn" has tag "PII" → needs encryption
   ↓
4. FieldEncryptionExecutor (Application A's instance)
   ├─ Uses DefaultAzureCredential (App A's Managed Identity)
   ├─ Authenticates to Azure Key Vault
   └─ Calls: "Encrypt '123-45-6789' using KEK 'customer-data-kek'"
   ↓
5. Azure Key Vault
   ├─ Verifies App A has permission to use customer-data-kek
   ├─ Uses KEK to encrypt "123-45-6789"
   └─ Returns: <encrypted bytes: 0xA7F3...>
   ↓
6. AvroSerializer
   ├─ Replaces plaintext SSN with encrypted bytes
   └─ Serializes to Avro binary: [Schema ID][...encrypted data...]
   ↓
7. Kafka
   Message stored: [Magic Byte: 0x00][Schema ID: 1][<encrypted binary>]
```

### Consumer Flow (Application B) - Detailed

```
1. Kafka
   Consumer receives: [0x00][Schema ID: 1][<encrypted binary>]
   ↓
2. AvroDeserializer
   ├─ Extracts Schema ID: 1
   ├─ Fetches schema from Schema Registry
   │  GET /schemas/ids/1
   └─ Receives schema with ruleset (same as producer)
   ↓
3. AvroDeserializer
   ├─ Identifies encrypted fields based on ruleset
  What this means:**
- Your FieldEncryptionExecutor is trying to access the KEK
- But your application's Managed Identity doesn't have permission
- The KEK exists, but you can't use it

**Solution:**
```bash
# Verify Managed Identity has permissions
az keyvault show-policy \
  --name shared-encryption-kv \
  --object-id $APP_A_IDENTITY

# Grant permissions if missing
az keyvault set-policy \
  --name shared-encryption-kv \
  --object-id $APP_A_IDENTITY \
  --key-permissions get wrapKey unwrapKey encrypt decrypt

# Verify the KEK exists
az keyvault key show \
  --vault-name shared-encryption-kv \
  --name customer-data-kek
6. AvroDeserializer
   ├─ Creates Customer object
   └─ Sets customer.Ssn = "123-45-6789"
   ↓
7. CustomerProcessor
   Receives Customer with decrypted PII (plaintext in memory)
```

### Key Points

**Application A and B use:**
- ✅ **Same KEK** (customer-data-kek in shared-encryption-kv)
- ✅ **Same ruleset** (from Schema Registry)
- ✅ **Same schema** (from Schema Registry)
- ❌ **Different credentials** (different Managed Identities)
- ❌ **Different FieldEncryptionExecutor instances** (own code)
- ❌ **Different app secrets** (different Key Vaults for config)

**The KEK never leaves Azure Key Vault!**
- Encryption/decryption happens inside Azure Key Vault
- Only encrypted bytes travel over the network
- Applications only get encrypted/decrypted results, never the KEK itself↓
4. Schema includes decryption ruleset
   ↓
5. Identifies encrypted fields
   ↓
6. Calls Azure Key Vault (using App B's Managed Identity)
   ↓
7. Decrypts SSN and CreditCard fields
   ↓
8. Returns Customer object with decrypted PII
   ↓
9.What this means:**
- Consumer's FieldEncryptionExecutor can't access the KEK
- Message is encrypted but can't be decrypted
- Application B doesn't have permission to use customer-data-kek

**Solution:**
```bash
# Ensure App B has access to the same KEK (not a different KEK!)
az keyvault set-policy \
  --name shared-encryption-kv \
  --object-id $APP_B_IDENTITY \
  --key-permissions get wrapKey unwrapKey encrypt decrypt

# Verify it's the same KEK that Application A uses
az keyvault key show \
  --vault-name shared-encryption-kv \
  --name customer-data-kek

# Test App B's access
az keyvault key show \
  --vault-name shared-encryption-kv \
  --name customer-data-kek \
  --query key.kid -o tsv
### Issue 1: Access Denied to Key Vault

**Error:** (for Kafka credentials, etc.)
3. **Share only the encryption KEK** in a shared Key Vault, not other secrets
4. **Understand what's shared vs separate:**
   - ✅ **Shared:** KEK (customer-data-kek), Schema Registry, Kafka cluster
   - ❌ **Separate:** Managed Identities, Kafka credentials, app secrets
5. **Use READ-only permissions** for consumer's Schema Registry access
6. **Rotate KEK** periodically (Azure Key Vault supports key versioning)
7. **Monitor Key Vault access logs** to track KEK usage
8. **Use network isolation** (VNets, Private Endpoints for Key Vault)
9. **Enable Key Vault soft delete** and purge protection
10. **Never log the KEK** or decrypted PII values
# Verify Managed Identity has permissions
az keyvault show-policy \
  --name shared-encryption-kv \ (READ only)
5. ❌ Don't share app-specific Key Vaults (only share the KEK vault)
6. ❌ Don't disable encryption for convenience
7. ❌ Don't log decrypted PII values
8. ❌ Don't confuse FieldEncryptionExecutor (engine) with KEK (key)
9. ❌ Don't store the KEK outside of Azure Key Vault
10. ❌ Don't give applications more Key Vault permissions than needed
az keyvault set-policy \
  --name shared-encryption-kv \
  --object-id $APP_A_IDENTITY \
  --key-permissions get wrapKey unwrapKey encrypt decrypt
```

### Issue 2: Schema Not Found

**Error:**
```
Schema not found for ID: 1
```

**Solution:**
```bash
# Re-register the schema with ruleset
curl -X POST http://schema-registry:8081/subjects/customer-events-value/versions \
  -H "Content-Type: application/json" \
  -d @schema-with-ruleset.json
```

### Issue 3: Different Apps See Different Secrets

**Verification:**
```bash
# Check App A can access its secrets
az keyvault secret show \
  --vault-name app-a-secrets-kv \
  --name kafka-username

# Check App B CANNOT access App A's secrets (should fail)
az keyvault secret show \
  --vault-name app-a-secrets-kv \
  --name kafka-username \
  --query value \
  --identity $APP_B_IDENTITY
# Expected: Access denied ✅
```

### Issue 4: Deserialization Fails

**Error:**
```
Failed to deserialize message: encryption key not available
```

**Solution:**
```bash
# Ensure App B has access to the same KEK
az keyvault set-policy \
  --name shared-encryption-kv \
  --object-id $APP_B_IDENTITY \
  --key-permissions get wrapKey unwrapKey encrypt decrypt
```

---

## Security Best Practices

### ✅ Do's

1. **Use separate Managed Identities** for each application
2. **Store app-specific secrets** in separate Key Vaults
3. **Share only the encryption KEK**, not other secrets
4. **Use READ-only permissions** for consumer's Schema Registry access
5. **Rotate credentials** regularly
6. **Monitor Key Vault access logs**
7. **Use network isolation** (VNets, Private Endpoints)
8. **Enable Key Vault soft delete** and purge protection

### ❌ Don'ts

1. ❌ Don't share Kafka credentials between apps
2. ❌ Don't use same Schema Registry API keys
3. ❌ Don't store secrets in code or config files
4. ❌ Don't grant App B WRITE access to Schema Registry
5. ❌ Don't share app-specific Key Vaults
6. ❌ Don't disable encryption for convenience
7. ❌ Don't log decrypted PII values

---

## Summary

**What's Shared:**
- ✅ Kafka cluster
- ✅ Schema Registry
- ✅ Encryption KEK in Azure Key Vault
- ✅ Schema definition with ruleset

**What's Separate:**
- ✅ Application A's Kafka credentials
- ✅ Application B's Kafka credentials
- ✅ Application A's Schema Registry API key
- ✅ Application B's Schema Registry API key
- ✅ Application A's Key Vault (for app secrets)
- ✅ Application B's Key Vault (for app secrets)
- ✅ Application A's Managed Identity
- ✅ Application B's Managed Identity

**Security Model:**
- Each app has its own credentials
- Each app authenticates independently
- Both apps can execute the same encryption ruleset
- Neither app can access the other's secrets
- Both apps can encrypt/decrypt using the shared KEK

This architecture provides **strong separation of concerns** while enabling **secure encrypted communication** between applications! 🔒
