/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
using System.Reflection;
using Mono.Cecil;
using de4dot.blocks;

namespace AssemblyData.methodsrewriter {
	class MType {
		public Type type;
		public TypeDefinition typeDefinition;
		Dictionary<int, MMethod> tokenToMethod;
		MethodDefinitionDict<MMethod> methodReferenceToMethod;
		Dictionary<int, MField> tokenToField;
		FieldDefinitionDict<MField> fieldReferenceToField;

		public MType(Type type, TypeDefinition typeDefinition) {
			this.type = type;
			this.typeDefinition = typeDefinition;
		}

		public MMethod getMethod(MethodReference methodReference) {
			initMethods();
			return methodReferenceToMethod.find(methodReference);
		}

		public MField getField(FieldReference fieldReference) {
			initFields();
			return fieldReferenceToField.find(fieldReference);
		}

		public MMethod getMethod(int token) {
			initMethods();
			return tokenToMethod[token];
		}

		public MField getField(int token) {
			initFields();
			return tokenToField[token];
		}

		void initMethods() {
			if (tokenToMethod != null)
				return;
			tokenToMethod = new Dictionary<int, MMethod>(typeDefinition.Methods.Count);
			methodReferenceToMethod = new MethodDefinitionDict<MMethod>();

			var tmpTokenToMethod = new Dictionary<int, MethodBase>();
			var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (var m in ResolverUtils.getMethodBases(type, flags))
				tmpTokenToMethod[m.MetadataToken] = m;
			foreach (var m in typeDefinition.Methods) {
				var token = m.MetadataToken.ToInt32();
				var method = new MMethod(tmpTokenToMethod[token], m);
				tokenToMethod[token] = method;
				methodReferenceToMethod.add(method.methodDefinition, method);
			}
		}

		void initFields() {
			if (tokenToField != null)
				return;
			tokenToField = new Dictionary<int, MField>(typeDefinition.Fields.Count);
			fieldReferenceToField = new FieldDefinitionDict<MField>();

			var tmpTokenToField = new Dictionary<int, FieldInfo>();
			var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (var f in type.GetFields(flags))
				tmpTokenToField[f.MetadataToken] = f;
			foreach (var f in typeDefinition.Fields) {
				var token = f.MetadataToken.ToInt32();
				var field = new MField(tmpTokenToField[token], f);
				tokenToField[token] = field;
				fieldReferenceToField.add(field.fieldDefinition, field);
			}
		}

		public override string ToString() {
			return string.Format("{0:X8} - {1}", typeDefinition.MetadataToken.ToUInt32(), typeDefinition.FullName);
		}
	}
}
