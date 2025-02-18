using Mono.Cecil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppModdingCodegen.Data.DllHandling
{
    internal class DllMethod : IMethod
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0052:Remove unread private members", Justification = "aids with debugging")]
        private readonly MethodDefinition This;

        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public int RVA { get; }
        public int Offset { get; }
        public int VA { get; }
        public int Slot { get; }
        public TypeRef ReturnType { get; }
        public TypeRef DeclaringType { get; }
        public TypeRef? ImplementedFrom { get; } = null;
        public List<IMethod> BaseMethods { get; private set; } = new List<IMethod>();
        public List<IMethod> ImplementingMethods { get; } = new List<IMethod>();
        public bool HidesBase { get; }
        public string Name { get; private set; }
        public string Il2CppName { get; }
        public List<Parameter> Parameters { get; } = new List<Parameter>();
        public bool Generic { get; }
        public IReadOnlyList<TypeRef> GenericParameters { get; }
        public bool IsSpecialName { get; }
        public bool IsVirtual { get; }

        // Use the specific hash comparer to ensure validity!
        private static readonly DllMethodDefinitionHash comparer = new DllMethodDefinitionHash();

        private static readonly Dictionary<MethodDefinition, DllMethod> cache = new Dictionary<MethodDefinition, DllMethod>(comparer);

        internal static DllMethod From(MethodDefinition def, ref HashSet<MethodDefinition> mappedBaseMethods)
        {
            // Note that TryGetValue is now significantly slower due to hash collisions and equality checks being expensive.
            // Before, it was simply pointers.
            if (cache.TryGetValue(def, out var m)) return m;
            return new DllMethod(def, ref mappedBaseMethods);
        }

        private DllMethod(MethodDefinition m, ref HashSet<MethodDefinition> mappedBaseMethods)
        {
            cache.Add(m, this);
            This = m;
            // Il2CppName is the MethodDefinition Name (hopefully we don't need to convert it for il2cpp, but we might)
            Il2CppName = m.Name;
            // Fuck you parens
            Name = m.Name.Replace('(', '_').Replace(')', '_');
            Parameters.AddRange(m.Parameters.Select(p => new Parameter(p)));
            Specifiers.AddRange(DllSpecifierHelpers.From(m));
            // This is not necessary: m.GenericParameters.Any(param => !m.DeclaringType.GenericParameters.Contains(param));
            Generic = m.HasGenericParameters;
            GenericParameters = m.GenericParameters.Select(g => DllTypeRef.From(g)).ToList();

            // This may not always be the case, we could have a special name in which case we have to do some sorcery
            // Grab the special name, grab the type from the special name
            // This only applies to methods that are not new slot
            if (!m.IsNewSlot)
            {
                int idxDot = Name.LastIndexOf('.');
                if (idxDot >= 2 && !Name.StartsWith("<"))
                {
                    // Call a utilities function for converting a special name method to a proper base method
                    var baseMethod = m.GetSpecialNameBaseMethod(out var iface, idxDot);
                    if (baseMethod is null) throw new Exception("Failed to find baseMethod for dotted method name!");
                    if (iface is null) throw new Exception("Failed to get iface for dotted method name!");
                    if (!mappedBaseMethods.Add(baseMethod))
                        throw new InvalidOperationException($"Base method: {baseMethod} has already been overriden!");
                    // Only one base method for special named methods
                    BaseMethods.Add(From(baseMethod, ref mappedBaseMethods));
                    ImplementedFrom = DllTypeRef.From(iface);
                    IsSpecialName = true;
                }
                else
                {
                    var baseMethod = m.GetBaseMethod();
                    if (baseMethod == m)
                    {
                        var baseMethods = m.GetBaseMethods();
                        if (baseMethods.Count > 0)
                            HidesBase = true;
                        // We need to check here SPECIFICALLY for a method in our declaring type that shares the same name as us, since we could have the same BaseMethod as it.
                        // If either ourselves or a method of the same safe name (after . prefixes) exists, we need to ensure that only the one with the dots gets the base method
                        // It correctly describes.
                        // Basically, we need to take all our specially named methods on our type that have already been defined and remove them from our current list of baseMethods.
                        // We should only ever have baseMethods of methods that are of methods that we haven't already used yet.
                        if (baseMethods.Count > 0)
                            foreach (var baseM in mappedBaseMethods)
                                baseMethods.Remove(baseM);
                        foreach (var bm in baseMethods)
                            BaseMethods.Add(From(bm, ref mappedBaseMethods));
                    }
                    else
                    {
                        if (!mappedBaseMethods.Add(baseMethod))
                            throw new InvalidOperationException($"Base method: {baseMethod} has already been overriden!");
                        BaseMethods.Add(From(baseMethod, ref mappedBaseMethods));
                    }
                }
            }
            if (BaseMethods.Count > 0)
            {
                // TODO: This may not be true for generic methods. Should ensure validity for IEnumerator<T> methods
                // This method is an implemented/overriden method.
                // TODO: We need to double check to see if we need multiple ImplementedFroms
                ImplementedFrom = BaseMethods.First().DeclaringType;
                // Add ourselves to our BaseMethod's ImplementingMethods
                foreach (var bm in BaseMethods)
                    bm.ImplementingMethods.Add(this);
            }

            ReturnType = DllTypeRef.From(m.ReturnType);
            DeclaringType = DllTypeRef.From(m.DeclaringType);
            // This is a very rare condition that we need to handle if it ever happens, but for now just log it
            if (m.HasOverrides)
                Console.WriteLine($"{m}.HasOverrides!!! Overrides: {string.Join(", ", m.Overrides)}");

            IsVirtual = m.IsVirtual || m.IsAbstract;

            RVA = -1;
            Offset = -1;
            VA = -1;
            Slot = -1;
            if (m.HasCustomAttributes)
                foreach (var ca in m.CustomAttributes)
                {
                    if (ca.AttributeType.Name == "AddressAttribute")
                    {
                        if (ca.Fields.Count >= 3)
                            for (int i = 0; i < ca.Fields.Count; i++)
                            {
                                var f = ca.Fields[i];
                                if (f.Name == "RVA" || f.Name == "Offset" || f.Name == "VA")
                                {
                                    var val = Convert.ToInt32(f.Argument.Value as string, 16);
                                    if (f.Name == "RVA") RVA = val;
                                    else if (f.Name == "Offset") Offset = val;
                                    else if (f.Name == "VA") VA = val;
                                }
                                else if (f.Name == "Slot")
                                    Slot = Convert.ToInt32(f.Argument.Value as string);
                            }
                    }
                    else
                    {
                        var atr = new DllAttribute(ca);
                        if (!string.IsNullOrEmpty(atr.Name))
                            Attributes.Add(atr);
                    }
                }
        }

        public override string ToString() => $"{ReturnType} {Name}({Parameters.FormatParameters()})";
    }
}