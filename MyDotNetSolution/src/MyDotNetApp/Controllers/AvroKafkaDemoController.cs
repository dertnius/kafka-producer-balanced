using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MyDotNetApp.Services;

namespace MyDotNetApp.Controllers;

/// <summary>
/// Demo controller showing real-world usage scenarios
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AvroKafkaDemoController : ControllerBase
{
    private readonly ILogger<AvroKafkaDemoController> _logger;
    private readonly IAvroKafkaProducerWithCSFLE _producer;

    public AvroKafkaDemoController(
        ILogger<AvroKafkaDemoController> logger,
        IAvroKafkaProducerWithCSFLE producer)
    {
        _logger = logger;
        _producer = producer;
    }

    /// <summary>
    /// Demo: Send a payment transaction with encrypted card data
    /// </summary>
    [HttpPost("demo/payment")]
    public async Task<IActionResult> SendPaymentDemo()
    {
        var sw = Stopwatch.StartNew();

        var paymentData = new
        {
            TransactionId = Guid.NewGuid().ToString(),
            CustomerId = "CUST-12345",
            CardNumber = "4111-1111-1111-1111",  // Sensitive - will be encrypted
            CVV = "123",                         // Sensitive - will be encrypted
            ExpiryDate = "12/25",               // Sensitive - will be encrypted
            Amount = 299.99m,
            Currency = "USD",
            MerchantId = "MERCH-789",
            Timestamp = DateTimeOffset.UtcNow
        };

        try
        {
            var result = await _producer.ProduceAsync(
                topic: "payments",
                key: paymentData.TransactionId,
                payload: paymentData,
                eventType: "payment.transaction.created",
                metadata: new Dictionary<string, string>
                {
                    ["source"] = "api",
                    ["version"] = "1.0",
                    ["environment"] = "demo"
                }
            );

            sw.Stop();

            return Ok(new
            {
                success = true,
                message = "Payment encrypted and sent to Kafka",
                details = new
                {
                    transactionId = paymentData.TransactionId,
                    topic = result.Topic,
                    partition = result.Partition.Value,
                    offset = result.Offset.Value,
                    timestamp = result.Timestamp.UnixTimestampMs,
                    processingTimeMs = sw.ElapsedMilliseconds,
                    encryptionEnabled = true
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send payment demo");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Demo: Send user PII data with encryption
    /// </summary>
    [HttpPost("demo/user-data")]
    public async Task<IActionResult> SendUserDataDemo()
    {
        var userData = new
        {
            UserId = Guid.NewGuid().ToString(),
            Email = "john.doe@example.com",
            FullName = "John Doe",
            SSN = "123-45-6789",              // Sensitive - encrypted
            DateOfBirth = "1990-05-15",      // Sensitive - encrypted
            PhoneNumber = "+1-555-0123",     // Sensitive - encrypted
            Address = new
            {
                Street = "123 Main St",
                City = "New York",
                State = "NY",
                ZipCode = "10001"
            }
        };

        try
        {
            var result = await _producer.ProduceAsync(
                topic: "user-events",
                key: userData.UserId,
                payload: userData,
                eventType: "user.profile.created"
            );

            return Ok(new
            {
                success = true,
                userId = userData.UserId,
                partition = result.Partition.Value,
                offset = result.Offset.Value
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send user data demo");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Demo: Send medical record with HIPAA-compliant encryption
    /// </summary>
    [HttpPost("demo/medical-record")]
    public async Task<IActionResult> SendMedicalRecordDemo()
    {
        var medicalRecord = new
        {
            RecordId = Guid.NewGuid().ToString(),
            PatientId = "PAT-98765",
            PatientName = "Jane Smith",
            MRN = "MRN-123456",              // Medical Record Number - encrypted
            Diagnosis = "Hypertension",      // PHI - encrypted
            Medications = new[] 
            { 
                "Lisinopril 10mg", 
                "Aspirin 81mg" 
            },                               // PHI - encrypted
            LabResults = new
            {
                BloodPressure = "120/80",
                Cholesterol = "180 mg/dL"
            },                               // PHI - encrypted
            ProviderId = "DOC-456",
            FacilityId = "HOSP-789",
            VisitDate = DateTimeOffset.UtcNow
        };

        try
        {
            var result = await _producer.ProduceAsync(
                topic: "healthcare-records",
                key: medicalRecord.RecordId,
                payload: medicalRecord,
                eventType: "medical.record.created",
                metadata: new Dictionary<string, string>
                {
                    ["compliance"] = "HIPAA",
                    ["classification"] = "PHI",
                    ["retention-period"] = "7-years"
                }
            );

            return Ok(new
            {
                success = true,
                recordId = medicalRecord.RecordId,
                topic = result.Topic,
                partition = result.Partition.Value,
                offset = result.Offset.Value,
                complianceNote = "All PHI data encrypted with CSFLE using Azure Key Vault"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send medical record demo");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Demo: Bulk send with performance metrics
    /// </summary>
    [HttpPost("demo/bulk-send")]
    public async Task<IActionResult> BulkSendDemo([FromQuery] int count = 100)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<object>();
        var errors = new List<string>();

        for (int i = 0; i < count; i++)
        {
            try
            {
                var data = new
                {
                    Id = i,
                    Timestamp = DateTimeOffset.UtcNow,
                    SensitiveField = $"encrypted-value-{i}",
                    RandomData = Guid.NewGuid().ToString()
                };

                var result = await _producer.ProduceAsync(
                    topic: "bulk-test",
                    key: $"key-{i}",
                    payload: data,
                    eventType: "bulk.test.event"
                );

                results.Add(new { id = i, offset = result.Offset.Value });
            }
            catch (Exception ex)
            {
                errors.Add($"Message {i}: {ex.Message}");
            }
        }

        sw.Stop();

        return Ok(new
        {
            success = true,
            totalRequested = count,
            totalSent = results.Count,
            totalFailed = errors.Count,
            totalTimeMs = sw.ElapsedMilliseconds,
            throughputMsgsPerSec = (results.Count / (sw.ElapsedMilliseconds / 1000.0)),
            averageLatencyMs = sw.ElapsedMilliseconds / (double)results.Count,
            results = results.Take(10), // Show first 10
            errors
        });
    }

    /// <summary>
    /// Test encryption round-trip
    /// </summary>
    [HttpGet("demo/test-encryption")]
    public async Task<IActionResult> TestEncryption()
    {
        var testData = new
        {
            Message = "This is a secret message",
            Timestamp = DateTimeOffset.UtcNow,
            Value = 42
        };

        try
        {
            var sw = Stopwatch.StartNew();

            var result = await _producer.ProduceAsync(
                topic: "encryption-test",
                key: "test-key",
                payload: testData,
                eventType: "encryption.test"
            );

            sw.Stop();

            return Ok(new
            {
                success = true,
                message = "Data encrypted and sent successfully",
                encryptionTimeMs = sw.ElapsedMilliseconds,
                kafkaResult = new
                {
                    topic = result.Topic,
                    partition = result.Partition.Value,
                    offset = result.Offset.Value
                },
                note = "Original data was encrypted with AES-256-CBC using Azure Key Vault"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Encryption test failed");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }
}
