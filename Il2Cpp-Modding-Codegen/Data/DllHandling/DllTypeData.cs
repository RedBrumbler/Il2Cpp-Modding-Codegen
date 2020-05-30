﻿using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Parsers;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DllHandling
{
    internal class DllTypeData : ITypeData
    {
        public TypeEnum Type { get; }
        public TypeInfo Info { get; }
        public TypeRef This { get; }
        public TypeRef Parent { get; }
        public List<TypeRef> ImplementingInterfaces { get; } = new List<TypeRef>();
        public int TypeDefIndex { get; }
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public List<IField> Fields { get; } = new List<IField>();
        public List<IProperty> Properties { get; } = new List<IProperty>();
        public List<IMethod> Methods { get; } = new List<IMethod>();

        private DllConfig _config;

        public DllTypeData(TypeDefinition def, DllConfig config)
        {
            _config = config;
            foreach (var i in def.Interfaces)
            {
                ImplementingInterfaces.Add(new TypeRef(i));
            }
            if (def.BaseType != null)
                Parent = new TypeRef(def.BaseType);

            This = new TypeRef(def);
            Type = def.IsEnum ? TypeEnum.Enum : def.IsInterface ? TypeEnum.Interface : def.IsClass ? TypeEnum.Class : TypeEnum.Struct;
            Info = new TypeInfo
            {
                TypeFlags = Type == TypeEnum.Class || Type == TypeEnum.Interface ? TypeFlags.ReferenceType : TypeFlags.ValueType
            };

            // TODO: Parse this eventually
            TypeDefIndex = -1;
            if (def.HasCustomAttributes && _config.ParseTypeAttributes)
                Attributes.AddRange(def.CustomAttributes.Select(ca => new DllAttribute(ca)));

            if (_config.ParseTypeFields)
                Fields.AddRange(def.Fields.Select(f => new DllField(f)));
            if (_config.ParseTypeProperties)
                Properties.AddRange(def.Properties.Select(p => new DllProperty(p)));
            if (_config.ParseTypeMethods)
                Methods.AddRange(def.Methods.Select(m => new DllMethod(m)));
        }

        public override string ToString()
        {
            var s = $"// Namespace: {This.Namespace}\n";
            foreach (var attr in Attributes)
            {
                s += $"{attr}\n";
            }
            foreach (var spec in Specifiers)
            {
                s += $"{spec} ";
            }
            s += $"{Type.ToString().ToLower()} {This.Name}";
            if (Parent != null)
            {
                s += $" : {Parent}";
            }
            s += "\n{";
            if (Fields.Count > 0)
            {
                s += "\n\t// Fields\n\t";
                foreach (var f in Fields)
                {
                    s += $"{f}\n\t";
                }
            }
            if (Properties.Count > 0)
            {
                s += "\n\t// Properties\n\t";
                foreach (var p in Properties)
                {
                    s += $"{p}\n\t";
                }
            }
            if (Methods.Count > 0)
            {
                s += "\n\t// Methods\n\t";
                foreach (var m in Methods)
                {
                    s += $"{m}\n\t";
                }
            }
            s = s.TrimEnd('\t');
            s += "}";
            return s;
        }
    }
}