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
using dot10.DotNet;
using de4dot.blocks;

namespace AssemblyData.methodsrewriter {
	class MType {
		public Type type;
		public TypeDef typeDef;
		Dictionary<int, MMethod> tokenToMethod;
		MethodDefinitionDict<MMethod> methodReferenceToMethod;
		Dictionary<int, MField> tokenToField;
		FieldDefinitionDict<MField> fieldReferenceToField;

		public MType(Type type, TypeDef typeDefinition) {
			this.type = type;
			this.typeDef = typeDefinition;
		}

		public MMethod getMethod(IMethod methodRef) {
			initMethods();
			return methodReferenceToMethod.find(methodRef);
		}

		public MField getField(IField fieldRef) {
			initFields();
			return fieldReferenceToField.find(fieldRef);
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
			tokenToMethod = new Dictionary<int, MMethod>(typeDef.Methods.Count);
			methodReferenceToMethod = new MethodDefinitionDict<MMethod>();

			var tmpTokenToMethod = new Dictionary<int, MethodBase>();
			var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (var m in ResolverUtils.getMethodBases(type, flags))
				tmpTokenToMethod[m.MetadataToken] = m;
			foreach (var m in typeDef.Methods) {
				var token = (int)m.MDToken.Raw;
				var method = new MMethod(tmpTokenToMethod[token], m);
				tokenToMethod[token] = method;
				methodReferenceToMethod.add(method.methodDef, method);
			}
		}

		void initFields() {
			if (tokenToField != null)
				return;
			tokenToField = new Dictionary<int, MField>(typeDef.Fields.Count);
			fieldReferenceToField = new FieldDefinitionDict<MField>();

			var tmpTokenToField = new Dictionary<int, FieldInfo>();
			var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (var f in type.GetFields(flags))
				tmpTokenToField[f.MetadataToken] = f;
			foreach (var f in typeDef.Fields) {
				var token = (int)f.MDToken.Raw;
				var field = new MField(tmpTokenToField[token], f);
				tokenToField[token] = field;
				fieldReferenceToField.add(field.fieldDef, field);
			}
		}

		public override string ToString() {
			return string.Format("{0:X8} - {1}", typeDef.MDToken.Raw, typeDef.FullName);
		}
	}
}
