# CSFLE Testing Guide - Verify Encryption is Working

## Quick Start

### Run All Tests
```bash
cd MyDotNetSolution
dotnet test Tests/CSFLEIntegrationTests.cs -v normal
```

### Run Specific Test
```bash
dotnet test Tests/CSFLEIntegrationTests.cs -k "Encryption_ShouldIncreasePayloadSize" -v normal
```

### Run with Detailed Output
```bash
dotnet test Tests/CSFLEIntegrationTests.cs -v detailed
```

---

## Test Categories

### 🟢 Unit Tests (No Dependencies - Always Run)

These tests verify CSFLE fundamentals and require NO Kafka/Schema Registry.

#### **TEST 1: Encryption Increases Payload Size**
```
✅ Tests that plaintext < ciphertext (encryption has overhead)
✅ Proves encryption is actually happening (not a no-op)
```

Run:
```bash
dotnet test -k "Encryption_ShouldIncreasePayloadSize"
```

**Expected Result:**
```
PASS: Encrypted (384 bytes) > plaintext (256 bytes)
```

---

#### **TEST 2: Encryption Round-Trip (Encrypt → Decrypt)**
```
✅ Encrypts data, then decrypts it
✅ Verifies original plaintext is recovered
```

Run:
```bash
dotnet test -k "Encryption_RoundTrip_ShouldRecoverOriginalData"
```

**Expected Result:**
```
PASS: Decrypted data == Original plaintext
```

---

#### **TEST 3: IV Randomization**
```
✅ Encrypting same plaintext twice produces different ciphertexts
✅ Proves IV (initialization vector) is randomized each time
```

Run:
```bash
dotnet test -k "Encryption_WithRandomIV_ShouldProduceDifferentCiphertexts"
```

**Expected Result:**
```
PASS: encrypted1 ≠ encrypted2 (due to random IV)
```

---

#### **TEST 4: Avro Message Structure**
```
✅ Verifies EncryptedAvroMessage has all required fields
```

Run:
```bash
dotnet test -k "EncryptedAvroMessage_ShouldHaveAllRequiredFields"
```

**Expected Result:**
```
PASS: All fields present and populated
```

---

#### **TEST 5: Producer Health Check**
```
✅ Verifies producer is initialized and healthy
```

Run:
```bash
dotnet test -k "Producer_IsHealthy_ShouldReturnTrue"
```

**Expected Result:**
```
PASS: Producer is healthy
```

---

### 🟡 Negative Tests (No Dependencies - Validate Error Handling)

#### **TEST 8: Tampering Detection**
```
✅ Modifies encrypted data (tamper attack)
✅ Verifies decryption FAILS (integrity check)
```

Run:
```bash
dotnet test -k "Decryption_WithTamperedData_ShouldFail"
```

**Expected Result:**
```
PASS: Throws Exception on tampered data
```

---

#### **TEST 9: Wrong IV Fails**
```
✅ Uses incorrect IV to decrypt
✅ Verifies decryption FAILS
```

Run:
```bash
dotnet test -k "Decryption_WithWrongIV_ShouldFail"
```

**Expected Result:**
```
PASS: Throws Exception with wrong IV
```

---

### 🔵 Integration Tests (Requires Kafka + Schema Registry)

These tests require:
- ✅ Kafka broker on `localhost:9092`
- ✅ Confluent Schema Registry on `localhost:8081`

#### **TEST 6: End-to-End Produce**
```
✅ Produces encrypted message to real Kafka
✅ Verifies message appears in topic
```

Run:
```bash
# First enable (uncomment Skip attribute)
dotnet test -k "EndToEnd_ProduceEncrypted_MessageShouldBeInKafka"
```

**Before Running:**
```bash
# Verify Kafka is running
kafka-broker-api-versions.sh --bootstrap-server localhost:9092

# Verify Schema Registry is running
curl http://localhost:8081/subjects
```

**Expected Result:**
```
PASS: DeliveryResult with Partition >= 0, Offset >= 0
PASS: Message.EncryptedPayload ≠ plaintext
```

---

#### **TEST 7: Message Headers**
```
✅ Verifies CSFLE headers are present
```

**Headers Should Include:**
- `encryption: CSFLE-AKV`
- `key-id: dek-kafka-csfle`
- `event-type: <event>`

---

### 🟠 Performance Tests

#### **TEST 10: Encryption Speed**
```
✅ Measures encryption performance
✅ Should complete < 500ms
```

Run:
```bash
dotnet test -k "Encryption_Performance_ShouldBeFast"
```

**Expected Result:**
```
PASS: Encryption < 500ms (typically 100-150ms)
```

---

## Complete Test Matrix

| Test | Category | Requires | What It Proves |
|------|----------|----------|---|
| #1 | Unit | No | Encryption adds overhead |
| #2 | Unit | No | Decrypt recovers plaintext |
| #3 | Unit | No | IV is randomized |
| #4 | Unit | No | Message structure is correct |
| #5 | Unit | No | Producer is initialized |
| #6 | Integration | Kafka | End-to-end works |
| #7 | Integration | Kafka | Headers are present |
| #8 | Negative | No | Tampering detected |
| #9 | Negative | No | Wrong IV fails |
| #10 | Performance | No | Encryption is fast |
| #11 | Validation | No | Null/empty handled |

---

## Run Test Suites

### Unit Tests Only (Fast - 2-3 seconds)
```bash
dotnet test Tests/CSFLEIntegrationTests.cs::MyDotNetApp.Tests.CSFLEIntegrationTests -v normal
```

### All Tests (Skip Integration)
```bash
dotnet test Tests/CSFLEIntegrationTests.cs -v normal --filter "FullyQualifiedName!~EndToEnd"
```

### Only Integration Tests (Requires Kafka)
```bash
# First, enable Skip attribute on integration tests
dotnet test Tests/CSFLEIntegrationTests.cs -v normal --filter "FullyQualifiedName~EndToEnd"
```

---

## Example Test Output

### Successful Test Run
```
Starting test execution...

 ✓ CSFLEIntegrationTests.Encryption_ShouldIncreasePayloadSize
   Encrypted (384) > Original (256) ✓

 ✓ CSFLEIntegrationTests.Encryption_RoundTrip_ShouldRecoverOriginalData
   "Sensitive message" → encrypt → decrypt → "Sensitive message" ✓

 ✓ CSFLEIntegrationTests.Encryption_WithRandomIV_ShouldProduceDifferentCiphertexts
   IV1 ≠ IV2, Ciphertext1 ≠ Ciphertext2 ✓

 ✓ CSFLEIntegrationTests.Decryption_WithTamperedData_ShouldFail
   Tampered data → throws Exception ✓

 ✓ CSFLEIntegrationTests.Encryption_Performance_ShouldBeFast
   10KB payload encrypted in 145ms ✓

Passed:  9
Failed:  0
Skipped: 2 (EndToEnd tests require Kafka)

Test execution completed in 2.341 seconds
```

---

## How to Interpret Results

### ✅ PASS = CSFLE is Working
```
✓ Encryption_ShouldIncreasePayloadSize
✓ Encryption_RoundTrip_ShouldRecoverOriginalData
✓ Encryption_WithRandomIV_ShouldProduceDifferentCiphertexts
```

### ❌ FAIL = Something Wrong
```
✗ Encryption_ShouldIncreasePayloadSize
  Expected: encryptedSize > plaintextSize
  Actual: encryptedSize == plaintextSize
  → Encryption is NOT happening (no-op)
```

### ⓘ SKIP = Not Running (Requires Kafka)
```
⊘ EndToEnd_ProduceEncrypted_MessageShouldBeInKafka
  Reason: Requires Kafka + Schema Registry
  → Start Kafka, then uncomment Skip attribute
```

---

## Troubleshooting Test Failures

### "Encryption_ShouldIncreasePayloadSize" FAILS
**Problem:** Encrypted size == plaintext size
**Cause:** Encryption not happening

**Fix:**
1. Check Azure Key Vault is accessible: `az login`
2. Check AzureKeyVaultService is registered in DI
3. Check debug logs for encryption errors

---

### "Encryption_RoundTrip" FAILS
**Problem:** Decrypted data ≠ original plaintext
**Cause:** Encryption/decryption mismatch

**Fix:**
1. Verify IV is being used correctly
2. Verify same KEK is used for encrypt/decrypt
3. Check Azure Key Vault encryption/decryption permissions

---

### Integration Tests FAIL or SKIP
**Problem:** EndToEnd tests won't run
**Cause:** Kafka not available

**Fix:**
```bash
# Start Kafka locally
docker run --name kafka -d -p 9092:9092 confluentinc/cp-kafka:latest

# Start Schema Registry
docker run --name schema-registry -d -p 8081:8081 confluentinc/cp-schema-registry:latest

# Then uncomment Skip attribute on integration tests
```

---

## Verification Checklist

After running ALL tests, verify:

- [ ] ✅ Unit tests all PASS (tests 1-5, 8-11)
- [ ] ✅ Encryption size increases (test 1)
- [ ] ✅ Round-trip works (test 2)
- [ ] ✅ IV is randomized (test 3)
- [ ] ✅ Tampering detected (test 8)
- [ ] ✅ Wrong IV fails (test 9)
- [ ] ✅ Performance < 500ms (test 10)
- [ ] ✅ (Optional) EndToEnd works with Kafka (test 6-7)

If all checks ✅, **CSFLE is working correctly**. 🎉

---

## Command Reference

```bash
# Quick check (2-3 seconds)
dotnet test Tests/CSFLEIntegrationTests.cs -k "Encryption_ShouldIncreasePayloadSize"

# Full unit test suite
dotnet test Tests/CSFLEIntegrationTests.cs -v normal

# With detailed output
dotnet test Tests/CSFLEIntegrationTests.cs -v detailed

# List all tests
dotnet test Tests/CSFLEIntegrationTests.cs --collect:"XPlat Code Coverage"

# Run specific category
dotnet test Tests/CSFLEIntegrationTests.cs -k "RoundTrip or Tampering or Performance"
```

---

**Last Updated:** February 2026  
**Test Count:** 11+ tests
