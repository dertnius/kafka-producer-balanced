using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MyDotNetApp.Optimization;
using Xunit;

namespace MyDotNetApp.Tests
{
    public class MortgagesDataFixture
    {
        public List<MortgageAsset> Assets { get; }
        public decimal Capacity { get; }

        public MortgagesDataFixture()
        {
            Capacity = 400_000m;
            var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "mortgages.json");
            var json = File.ReadAllText(path);
            Assets = JsonSerializer.Deserialize<List<MortgageAsset>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<MortgageAsset>();
        }
    }

    [CollectionDefinition("KnapsackMortgageCollection")]
    public class KnapsackMortgageCollection : ICollectionFixture<MortgagesDataFixture>
    {
    }
}
