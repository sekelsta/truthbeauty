using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Collections.Generic;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Genelib {
    public class GenomeType {
        public enum GeneLocation {
            Autosomal,
            XZ,
            YW
        }

        private struct NameMapping {
            public Dictionary<string, int> geneMap;
            public string[] geneArray;
            public string[][] alleleArrays;
            public Dictionary<string, int>[] alleleMaps;
        }

        private static Dictionary<AssetLocation, GenomeType> loaded = new Dictionary<AssetLocation, GenomeType>();

        private NameMapping autosomal;
        private NameMapping xz;
        private NameMapping yw;
        private Dictionary<string, GeneInitializer> initializers = new Dictionary<string, GeneInitializer>();

        public SexDetermination SexDetermination { get; protected set; } = SexDetermination.XY;
        private AlleleFrequencies defaultFrequencies;
        public AlleleFrequencies DefaultFrequencies {
            get {
                if (defaultFrequencies == null) {
                    defaultFrequencies = new AlleleFrequencies(this);
                }
                return defaultFrequencies;
                }
            private set {
                defaultFrequencies = value;
            }
        }
        public string Name { get; private set; }
        public int AutosomalGeneCount { get => autosomal.geneArray.Length; }
        public int XZGeneCount { get => xz.geneArray.Length; }
        public int YWGeneCount { get => yw.geneArray.Length; }

        private GenomeType(string name, JsonObject attributes) {
            this.Name = name;
            JsonObject genes = attributes["genes"];
            parse(genes, "autosomal", ref autosomal);
            parse(genes, "xz", ref xz);
            parse(genes, "yw", ref yw);

            JsonObject initialization = attributes["initializers"];
            foreach (JProperty jp in ((JObject) initialization.Token).Properties()) {
                initializers[jp.Name] = new GeneInitializer(this, new JsonObject(jp.Value));
            }
            if (attributes.KeyExists("sexdetermination")) {
                SexDetermination = SexDeterminationExtensions.Parse(attributes["sexdetermination"].AsString());
            }
        }

        private void parse(JsonObject json, string key, ref NameMapping mapping) {
            if (!json.KeyExists(key)) {
                mapping.geneArray = new string[0];
                return;
            }
            JsonObject[] genes = json[key].AsArray();
            mapping.geneMap = new Dictionary<string, int>();
            mapping.geneArray = new string[genes.Length];
            mapping.alleleArrays = new string[genes.Length][];
            mapping.alleleMaps = new Dictionary<string, int>[genes.Length];
            for (int gene = 0; gene < genes.Length; ++gene) {
                JProperty jp = ((JObject) genes[gene].Token).Properties().First();
                string geneName = jp.Name;
                mapping.geneMap[geneName] = gene;
                mapping.geneArray[gene] = geneName;
                mapping.alleleArrays[gene] = new JsonObject(jp.Value).AsArray<string>();
                mapping.alleleMaps[gene] = new Dictionary<string, int>();
                for (int allele = 0; allele < mapping.alleleArrays[gene].Length; ++allele) {
                    mapping.alleleMaps[gene][mapping.alleleArrays[gene][allele]] = allele;
                }
            }
        }

        public static GenomeType FromLocation(AssetLocation location) {
            if (!loaded.ContainsKey(location)) {
                AssetLocation path = location.CopyWithPathPrefixAndAppendix("genetics/", ".json");
                loaded[location] = new GenomeType(location.ToString(), JsonObject.FromJson(GeneticsModSystem.API.Assets.Get(path).ToText()));
            }
            return loaded[location];
        }

        public int GeneID(string name) {
            return autosomal.geneMap[name];
        }

        public string GeneName(int id) {
            return autosomal.geneArray[id];
        }

        public string AlleleName(int geneID, int alleleID) {
            return autosomal.alleleArrays[geneID][alleleID];
        }

        public int AlleleID(int geneID, string alleleName) {
            return autosomal.alleleMaps[geneID][alleleName];
        }

        public int XZGeneID(string name) {
            return xz.geneMap[name];
        }

        public string XZGeneName(int id) {
            return xz.geneArray[id];
        }

        public string XZAlleleName(int geneID, int alleleID) {
            return xz.alleleArrays[geneID][alleleID];
        }

        public int XZAlleleID(int geneID, string alleleName) {
            return xz.alleleMaps[geneID][alleleName];
        }

        public int YWGeneID(string name) {
            return yw.geneMap[name];
        }

        public string YWGeneName(int id) {
            return yw.geneArray[id];
        }

        public string YWAlleleName(int geneID, int alleleID) {
            return yw.alleleArrays[geneID][alleleID];
        }

        public int YWAlleleID(int geneID, string alleleName) {
            return yw.alleleMaps[geneID][alleleName];
        }

        public int GeneID(string name, GeneLocation location) {
            if (location == GeneLocation.Autosomal) {
                return GeneID(name);
            }
            if (location == GeneLocation.XZ) {
                return XZGeneID(name);
            }
            if (location == GeneLocation.YW) {
                return YWGeneID(name);
            }
            throw new ArgumentException("Unrecognized GeneLocation", nameof(location));
        }

        public string GeneName(int id, GeneLocation location) {
            if (location == GeneLocation.Autosomal) {
                return GeneName(id);
            }
            if (location == GeneLocation.XZ) {
                return XZGeneName(id);
            }
            if (location == GeneLocation.YW) {
                return YWGeneName(id);
            }
            throw new ArgumentException("Unrecognized GeneLocation", nameof(location));
        }

        public string AlleleName(int geneID, int alleleID, GeneLocation location) {
            if (location == GeneLocation.Autosomal) {
                return AlleleName(geneID, alleleID);
            }
            if (location == GeneLocation.XZ) {
                return XZAlleleName(geneID, alleleID);
            }
            if (location == GeneLocation.YW) {
                return YWAlleleName(geneID, alleleID);
            }
            throw new ArgumentException("Unrecognized GeneLocation", nameof(location));
        }

        public int AlleleID(int geneID, string alleleName, GeneLocation location) {
            if (location == GeneLocation.Autosomal) {
                return AlleleID(geneID, alleleName);
            }
            if (location == GeneLocation.XZ) {
                return XZAlleleID(geneID, alleleName);
            }
            if (location == GeneLocation.YW) {
                return YWAlleleID(geneID, alleleName);
            }
            throw new ArgumentException("Unrecognized GeneLocation", nameof(location));
        }

        public GeneInitializer Initializer(string name) {
            return initializers[name];
        }

        public AlleleFrequencies ChooseInitializer(string[] initializerNames, ClimateCondition climate, int y, Random random) {
            List<GeneInitializer> valid = new List<GeneInitializer>();
            if (initializerNames != null) {
                for (int i = 0; i < initializerNames.Length; ++i) {
                    GeneInitializer ini = initializers[initializerNames[i]];
                    if (ini.CanSpawnAt(climate, y)) {
                        valid.Add(ini);
                    }
                }
            }
            if (valid.Count == 0) {
                return null;
            }
            return valid[random.Next(valid.Count)].Frequencies;
        }
    }
}
