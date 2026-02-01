using System;
using Avro;
using Avro.Specific;

namespace MyDotNetApp.Models;

/// <summary>
/// Avro message with encrypted sensitive fields
/// Schema compatible with Confluent Schema Registry
/// </summary>
public class EncryptedAvroMessage : ISpecificRecord
{
    public static Schema _SCHEMA = Schema.Parse(@"{
        ""type"": ""record"",
        ""name"": ""EncryptedAvroMessage"",
        ""namespace"": ""MyDotNetApp.Models"",
        ""fields"": [
            { ""name"": ""id"", ""type"": ""long"" },
            { ""name"": ""timestamp"", ""type"": ""long"" },
            { ""name"": ""eventType"", ""type"": ""string"" },
            { ""name"": ""encryptedPayload"", ""type"": ""bytes"" },
            { ""name"": ""keyId"", ""type"": ""string"" },
            { ""name"": ""encryptionAlgorithm"", ""type"": ""string"" },
            { ""name"": ""iv"", ""type"": ""bytes"" },
            { ""name"": ""metadata"", ""type"": [""null"", ""string""], ""default"": null }
        ]
    }");

    public Schema Schema => _SCHEMA;

    public long Id { get; set; }
    public long Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty;
    public byte[] EncryptedPayload { get; set; } = Array.Empty<byte>();
    public string KeyId { get; set; } = string.Empty;
    public string EncryptionAlgorithm { get; set; } = "AES256-GCM";
    public byte[] IV { get; set; } = Array.Empty<byte>();
    public string? Metadata { get; set; }

    public object Get(int fieldPos)
    {
        return fieldPos switch
        {
            0 => Id,
            1 => Timestamp,
            2 => EventType,
            3 => EncryptedPayload,
            4 => KeyId,
            5 => EncryptionAlgorithm,
            6 => IV,
            7 => Metadata ?? null!,
            _ => throw new AvroRuntimeException($"Bad index {fieldPos}")
        };
    }

    public void Put(int fieldPos, object fieldValue)
    {
        switch (fieldPos)
        {
            case 0: Id = (long)fieldValue; break;
            case 1: Timestamp = (long)fieldValue; break;
            case 2: EventType = (string)fieldValue; break;
            case 3: EncryptedPayload = (byte[])fieldValue; break;
            case 4: KeyId = (string)fieldValue; break;
            case 5: EncryptionAlgorithm = (string)fieldValue; break;
            case 6: IV = (byte[])fieldValue; break;
            case 7: Metadata = (string?)fieldValue; break;
            default: throw new AvroRuntimeException($"Bad index {fieldPos}");
        }
    }
}
