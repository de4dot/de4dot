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
using System.Reflection;
using dnlib.DotNet;
using de4dot.blocks;

namespace AssemblyData.methodsrewriter {
	class MType {
		public Type type;
		public TypeDef typeDef;
		Dictionary<int, MMethod> tokenToMethod;
		MethodDefDict<MMethod> methodRefToMethod;
		Dictionary<int, MField> tokenToField;
		FieldDefDict<MField> fieldRefToField;

		public MType(Type type, TypeDef typeDef) {
			this.type = type;
			this.typeDef = typeDef;
		}

		public MMethod GetMethod(IMethod methodRef) {
			InitMethods();
			return methodRefToMethod.Find(methodRef);
		}

		public MField GetField(IField fieldRef) {
			InitFields();
			return fieldRefToField.Find(fieldRef);
		}

		public MMethod GetMethod(int token) {
			InitMethods();
			return tokenToMethod[token];
		}

		public MField GetField(int token) {
			InitFields();
			return tokenToField[token];
		}

		void InitMethods() {
			if (tokenToMethod != null)
				return;
			tokenToMethod = new Dictionary<int, MMethod>(typeDef.Methods.Count);
			methodRefToMethod = new MethodDefDict<MMethod>();

			var tmpTokenToMethod = new Dictionary<int, MethodBase>();
			var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (var m in ResolverUtils.GetMethodBases(type, flags))
				tmpTokenToMethod[m.MetadataToken] = m;
			foreach (var m in typeDef.Methods) {
				var token = (int)m.MDToken.Raw;
				var method = new MMethod(tmpTokenToMethod[token], m);
				tokenToMethod[token] = method;
				methodRefToMethod.Add(method.methodDef, method);
			}
		}

		void InitFields() {
			if (tokenToField != null)
				return;
			tokenToField = new Dictionary<int, MField>(typeDef.Fields.Count);
			fieldRefToField = new FieldDefDict<MField>();

			var tmpTokenToField = new Dictionary<int, FieldInfo>();
			var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (var f in type.GetFields(flags))
				tmpTokenToField[f.MetadataToken] = f;
			foreach (var f in typeDef.Fields) {
				var token = (int)f.MDToken.Raw;
				var field = new MField(tmpTokenToField[token], f);
				tokenToField[token] = field;
				fieldRefToField.Add(field.fieldDef, field);
			}
		}

		public override string ToString() {
			return string.Format("{0:X8} - {1}", typeDef.MDToken.Raw, typeDef.FullName);
		}
	}
}
