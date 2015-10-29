/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.renamer.asmmodules {
	public class Module : IResolver {
		IObfuscatedFile obfuscatedFile;
		TypeDefDict types = new TypeDefDict();
		MemberRefFinder memberRefFinder;
		IList<RefToDef<TypeRef, TypeDef>> typeRefsToRename = new List<RefToDef<TypeRef, TypeDef>>();
		IList<RefToDef<MemberRef, MethodDef>> methodRefsToRename = new List<RefToDef<MemberRef, MethodDef>>();
		IList<RefToDef<MemberRef, FieldDef>> fieldRefsToRename = new List<RefToDef<MemberRef, FieldDef>>();
		List<CustomAttributeRef> customAttributeFieldRefs = new List<CustomAttributeRef>();
		List<CustomAttributeRef> customAttributePropertyRefs = new List<CustomAttributeRef>();
		List<MethodDef> allMethods;

		public class CustomAttributeRef {
			public CustomAttribute cattr;
			public int index;
			public IMemberRef reference;
			public CustomAttributeRef(CustomAttribute cattr, int index, IMemberRef reference) {
				this.cattr = cattr;
				this.index = index;
				this.reference = reference;
			}
		}

		public class RefToDef<R, D> where R : ICodedToken where D : ICodedToken {
			public R reference;
			public D definition;
			public RefToDef(R reference, D definition) {
				this.reference = reference;
				this.definition = definition;
			}
		}

		public IEnumerable<RefToDef<TypeRef, TypeDef>> TypeRefsToRename {
			get { return typeRefsToRename; }
		}

		public IEnumerable<RefToDef<MemberRef, MethodDef>> MethodRefsToRename {
			get { return methodRefsToRename; }
		}

		public IEnumerable<RefToDef<MemberRef, FieldDef>> FieldRefsToRename {
			get { return fieldRefsToRename; }
		}

		public IEnumerable<CustomAttributeRef> CustomAttributeFieldRefs {
			get { return customAttributeFieldRefs; }
		}

		public IEnumerable<CustomAttributeRef> CustomAttributePropertyRefs {
			get { return customAttributePropertyRefs; }
		}

		public IObfuscatedFile ObfuscatedFile {
			get { return obfuscatedFile; }
		}

		public string Filename {
			get { return obfuscatedFile.Filename; }
		}

		public ModuleDefMD ModuleDefMD {
			get { return obfuscatedFile.ModuleDefMD; }
		}

		public Module(IObfuscatedFile obfuscatedFile) {
			this.obfuscatedFile = obfuscatedFile;
		}

		public IEnumerable<MTypeDef> GetAllTypes() {
			return types.GetValues();
		}

		public IEnumerable<MethodDef> GetAllMethods() {
			return allMethods;
		}

		public void FindAllMemberRefs(ref int typeIndex) {
			memberRefFinder = new MemberRefFinder();
			memberRefFinder.FindAll(ModuleDefMD);
			allMethods = new List<MethodDef>(memberRefFinder.MethodDefs.Keys);

			var allTypesList = new List<MTypeDef>();
			foreach (var type in memberRefFinder.TypeDefs.Keys) {
				var typeDef = new MTypeDef(type, this, typeIndex++);
				types.Add(typeDef);
				allTypesList.Add(typeDef);
				typeDef.AddMembers();
			}

			var allTypesCopy = new List<MTypeDef>(allTypesList);
			var typeToIndex = new Dictionary<TypeDef, int>();
			for (int i = 0; i < allTypesList.Count; i++)
				typeToIndex[allTypesList[i].TypeDef] = i;
			foreach (var typeDef in allTypesList) {
				if (typeDef.TypeDef.NestedTypes == null)
					continue;
				foreach (var nestedTypeDef2 in typeDef.TypeDef.NestedTypes) {
					int index = typeToIndex[nestedTypeDef2];
					var nestedTypeDef = allTypesCopy[index];
					allTypesCopy[index] = null;
					if (nestedTypeDef == null)	// Impossible
						throw new ApplicationException("Nested type belongs to two or more types");
					typeDef.Add(nestedTypeDef);
					nestedTypeDef.NestingType = typeDef;
				}
			}
		}

		public void ResolveAllRefs(IResolver resolver) {
			foreach (var typeRef in memberRefFinder.TypeRefs.Keys) {
				var typeDef = resolver.ResolveType(typeRef);
				if (typeDef != null)
					typeRefsToRename.Add(new RefToDef<TypeRef, TypeDef>(typeRef, typeDef.TypeDef));
			}

			foreach (var memberRef in memberRefFinder.MemberRefs.Keys) {
				if (memberRef.IsMethodRef) {
					var methodDef = resolver.ResolveMethod(memberRef);
					if (methodDef != null)
						methodRefsToRename.Add(new RefToDef<MemberRef, MethodDef>(memberRef, methodDef.MethodDef));
				}
				else if (memberRef.IsFieldRef) {
					var fieldDef = resolver.ResolveField(memberRef);
					if (fieldDef != null)
						fieldRefsToRename.Add(new RefToDef<MemberRef, FieldDef>(memberRef, fieldDef.FieldDef));
				}
			}

			foreach (var cattr in memberRefFinder.CustomAttributes.Keys) {
				var typeDef = resolver.ResolveType(cattr.AttributeType);
				if (typeDef == null)
					continue;
				if (cattr.NamedArguments == null)
					continue;

				for (int i = 0; i < cattr.NamedArguments.Count; i++) {
					var namedArg = cattr.NamedArguments[i];
					if (namedArg.IsField) {
						var fieldDef = FindField(typeDef, namedArg.Name, namedArg.Type);
						if (fieldDef == null) {
							Logger.w("Could not find field {0} in attribute {1} ({2:X8})",
									Utils.ToCsharpString(namedArg.Name),
									Utils.ToCsharpString(typeDef.TypeDef.Name),
									typeDef.TypeDef.MDToken.ToInt32());
							continue;
						}

						customAttributeFieldRefs.Add(new CustomAttributeRef(cattr, i, fieldDef.FieldDef));
					}
					else {
						var propDef = FindProperty(typeDef, namedArg.Name, namedArg.Type);
						if (propDef == null) {
							Logger.w("Could not find property {0} in attribute {1} ({2:X8})",
									Utils.ToCsharpString(namedArg.Name),
									Utils.ToCsharpString(typeDef.TypeDef.Name),
									typeDef.TypeDef.MDToken.ToInt32());
							continue;
						}

						customAttributePropertyRefs.Add(new CustomAttributeRef(cattr, i, propDef.PropertyDef));
					}
				}
			}
		}

		static MFieldDef FindField(MTypeDef typeDef, UTF8String name, TypeSig fieldType) {
			while (typeDef != null) {
				foreach (var fieldDef in typeDef.AllFields) {
					if (fieldDef.FieldDef.Name != name)
						continue;
					if (new SigComparer().Equals(fieldDef.FieldDef.FieldSig.GetFieldType(), fieldType))
						return fieldDef;
				}

				if (typeDef.baseType == null)
					break;
				typeDef = typeDef.baseType.typeDef;
			}
			return null;
		}

		static MPropertyDef FindProperty(MTypeDef typeDef, UTF8String name, TypeSig propType) {
			while (typeDef != null) {
				foreach (var propDef in typeDef.AllProperties) {
					if (propDef.PropertyDef.Name != name)
						continue;
					if (new SigComparer().Equals(propDef.PropertyDef.PropertySig.GetRetType(), propType))
						return propDef;
				}

				if (typeDef.baseType == null)
					break;
				typeDef = typeDef.baseType.typeDef;
			}
			return null;
		}

		public void OnTypesRenamed() {
			var newTypes = new TypeDefDict();
			foreach (var typeDef in types.GetValues()) {
				typeDef.OnTypesRenamed();
				newTypes.Add(typeDef);
			}
			types = newTypes;

			ModuleDefMD.ResetTypeDefFindCache();
		}

		static ITypeDefOrRef GetNonGenericTypeRef(ITypeDefOrRef typeRef) {
			var ts = typeRef as TypeSpec;
			if (ts == null)
				return typeRef;
			var gis = ts.TryGetGenericInstSig();
			if (gis == null || gis.GenericType == null)
				return typeRef;
			return gis.GenericType.TypeDefOrRef;
		}

		public MTypeDef ResolveType(ITypeDefOrRef typeRef) {
			return this.types.Find(GetNonGenericTypeRef(typeRef));
		}

		public MMethodDef ResolveMethod(IMethodDefOrRef methodRef) {
			var typeDef = this.types.Find(GetNonGenericTypeRef(methodRef.DeclaringType));
			if (typeDef == null)
				return null;
			return typeDef.FindMethod(methodRef);
		}

		public MFieldDef ResolveField(MemberRef fieldRef) {
			var typeDef = this.types.Find(GetNonGenericTypeRef(fieldRef.DeclaringType));
			if (typeDef == null)
				return null;
			return typeDef.FindField(fieldRef);
		}
	}
}
