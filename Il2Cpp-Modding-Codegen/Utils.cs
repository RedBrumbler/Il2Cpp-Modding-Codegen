﻿using Il2Cpp_Modding_Codegen.Data;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Il2Cpp_Modding_Codegen
{
    internal static class Utils
    {
        public static string ReplaceFirst(this string text, string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0)
                return text;
            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }

        public static TypeDefinition ResolvedBaseType(this TypeDefinition self)
        {
            var base_type = self?.BaseType;
            if (base_type is null) return null;
            return base_type.Resolve();
        }

        private static Dictionary<string, TypeReference> GetGenerics(this TypeReference self, TypeDefinition templateType)
        {
            var map = new Dictionary<string, TypeReference>();
            if (!self.IsGenericInstance)
                return map;
            if (!(self is GenericInstanceType instance) || instance.GenericArguments.Count != templateType.GenericParameters.Count)
                // Mismatch of generic parameters. Presumably, resolved has some inherited generic parameters that it is not listing, although this should not happen.
                // Since !0 and !1 will occur in resolved.GenericParameters instead.
                throw new InvalidOperationException("instance.GenericArguments is either null or of a mismatching count compared to resolved.GenericParameters!");
            for (int i = 0; i < templateType.GenericParameters.Count; i++)
            {
                // Map from resolved generic parameter to self generic parameter
                map.Add(templateType.GenericParameters[i].Name, (self as GenericInstanceType).GenericArguments[i]);
            }
            return map;
        }

        private static bool QuickEquals(TypeReference r1, TypeReference r2)
        {
            return r1?.FullName == r2?.FullName;
        }

        // Returns all methods with the same name and parameters as `self` in any base type or interface of `type`.
        private static HashSet<MethodDefinition> FindIn(this MethodDefinition self, TypeDefinition type, Dictionary<string, TypeReference> genericMapping)
        {
            HashSet<MethodDefinition> matches = new HashSet<MethodDefinition>();
            if (type == null) return matches;
            if (type != self.DeclaringType)
            {
                var sName = self.Name.Substring(self.Name.LastIndexOf('.') + 1);
                foreach (var m in type.Methods)
                {
                    // We don't want to actually check the equivalence of these, we want to check to see if they mean the same thing.
                    // For example, if we have a T, we want to ensure that the Ts would match
                    // We need to ensure the name of both self and m are fixed to not have any ., use the last . and ignore generic parameters
                    if (m.Name.Substring(m.Name.LastIndexOf('.') + 1) != sName)
                        goto cont;
                    var ret = m.ReturnType;
                    if (genericMapping.TryGetValue(ret.Name, out var r2))
                        ret = r2;
                    // If ret == self.ReturnType, we have a match
                    if (!QuickEquals(ret, self.ReturnType))
                        goto cont;
                    if (m.Parameters.Count != self.Parameters.Count)
                        goto cont;
                    for (int i = 0; i < m.Parameters.Count; i++)
                    {
                        var arg = m.Parameters[i].ParameterType;
                        if (genericMapping.TryGetValue(m.Parameters[i].ParameterType.Name, out var a))
                            arg = a;
                        // If arg == self.Parameters[i].ParameterType, we have a match
                        if (!QuickEquals(arg, self.Parameters[i].ParameterType))
                            goto cont;
                    }
                    matches.Add(m);
                cont:;
                }
            }

            var bType = type.ResolvedBaseType();
            matches.UnionWith(self.FindIn(bType, type.GetGenerics(bType)));
            foreach (var @interface in type.Interfaces)
            {
                var resolved = @interface.InterfaceType.Resolve();
                matches.UnionWith(self.FindIn(resolved, @interface.InterfaceType.GetGenerics(resolved)));
            }
            return matches;
        }

        // Returns all methods with the same name, parameters, and return type as `self` in any base type or interface of `self.DeclaringType`.
        // Unlike Mono.Cecil.Rocks.MethodDefinitionRocks.GetBaseMethod, will never return `self`.
        /// <summary>
        /// Returns all methods with the same name, parameters, and return type as <paramref name="self"/> in any base type or interface of <see cref="MethodDefinition.DeclaringType"/>
        /// Returns an empty set if no matching methods are found. Does not include <paramref name="self"/> in the search.
        /// </summary>
        /// <param name="self"></param>
        /// <returns></returns>
        public static HashSet<MethodDefinition> GetBaseMethods(this MethodDefinition self)
        {
            Contract.Requires(self != null);
            return self.FindIn(self.DeclaringType, self.DeclaringType.GetGenerics(self.DeclaringType.Resolve()));
        }
    }
}