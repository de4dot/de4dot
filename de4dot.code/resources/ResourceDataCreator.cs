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
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using Mono.Cecil;

namespace de4dot.code.resources {
	class ResourceDataCreator {
		ModuleDefinition module;
		Dictionary<string, UserResourceType> dict = new Dictionary<string, UserResourceType>(StringComparer.Ordinal);
		Dictionary<string, string> asmNameToAsmFullName = new Dictionary<string, string>(StringComparer.Ordinal);

		public ResourceDataCreator(ModuleDefinition module) {
			this.module = module;
		}

		public int Count {
			get { return dict.Count; }
		}

		public BuiltInResourceData createNull() {
			return new BuiltInResourceData(ResourceTypeCode.Null, null);
		}

		public BuiltInResourceData create(string value) {
			return new BuiltInResourceData(ResourceTypeCode.String, value);
		}

		public BuiltInResourceData create(bool value) {
			return new BuiltInResourceData(ResourceTypeCode.Boolean, value);
		}

		public BuiltInResourceData create(char value) {
			return new BuiltInResourceData(ResourceTypeCode.Char, value);
		}

		public BuiltInResourceData create(byte value) {
			return new BuiltInResourceData(ResourceTypeCode.Byte, value);
		}

		public BuiltInResourceData create(sbyte value) {
			return new BuiltInResourceData(ResourceTypeCode.SByte, value);
		}

		public BuiltInResourceData create(short value) {
			return new BuiltInResourceData(ResourceTypeCode.Int16, value);
		}

		public BuiltInResourceData create(ushort value) {
			return new BuiltInResourceData(ResourceTypeCode.UInt16, value);
		}

		public BuiltInResourceData create(int value) {
			return new BuiltInResourceData(ResourceTypeCode.Int32, value);
		}

		public BuiltInResourceData create(uint value) {
			return new BuiltInResourceData(ResourceTypeCode.UInt32, value);
		}

		public BuiltInResourceData create(long value) {
			return new BuiltInResourceData(ResourceTypeCode.Int64, value);
		}

		public BuiltInResourceData create(ulong value) {
			return new BuiltInResourceData(ResourceTypeCode.UInt64, value);
		}

		public BuiltInResourceData create(float value) {
			return new BuiltInResourceData(ResourceTypeCode.Single, value);
		}

		public BuiltInResourceData create(double value) {
			return new BuiltInResourceData(ResourceTypeCode.Double, value);
		}

		public BuiltInResourceData create(decimal value) {
			return new BuiltInResourceData(ResourceTypeCode.Decimal, value);
		}

		public BuiltInResourceData create(DateTime value) {
			return new BuiltInResourceData(ResourceTypeCode.DateTime, value);
		}

		public BuiltInResourceData create(TimeSpan value) {
			return new BuiltInResourceData(ResourceTypeCode.TimeSpan, value);
		}

		public BuiltInResourceData create(byte[] value) {
			return new BuiltInResourceData(ResourceTypeCode.ByteArray, value);
		}

		public CharArrayResourceData create(char[] value) {
			return new CharArrayResourceData(createUserResourceType(CharArrayResourceData.typeName), value);
		}

		public IconResourceData createIcon(byte[] value) {
			return new IconResourceData(createUserResourceType(IconResourceData.typeName), value);
		}

		public ImageResourceData createImage(byte[] value) {
			return new ImageResourceData(createUserResourceType(ImageResourceData.typeName), value);
		}

		public BinaryResourceData createSerialized(byte[] value) {
			string assemblyName, typeName;
			if (!getSerializedTypeAndAssemblyName(value, out assemblyName, out typeName))
				throw new ApplicationException("Could not get serialized type name");
			string fullName = string.Format("{0},{1}", typeName, assemblyName);
			return new BinaryResourceData(createUserResourceType(fullName), value);
		}

		class MyBinder : SerializationBinder {
			public class OkException : Exception {
				public string AssemblyName { get; set; }
				public string TypeName { get; set; }
			}

			public override Type BindToType(string assemblyName, string typeName) {
				throw new OkException {
					AssemblyName = assemblyName,
					TypeName = typeName,
				};
			}
		}

		bool getSerializedTypeAndAssemblyName(byte[] value, out string assemblyName, out string typeName) {
			try {
				var formatter = new BinaryFormatter();
				formatter.Binder = new MyBinder();
				formatter.Deserialize(new MemoryStream(value));
			}
			catch (MyBinder.OkException ex) {
				assemblyName = ex.AssemblyName;
				typeName = ex.TypeName;
				return true;
			}
			catch {
			}

			assemblyName = null;
			typeName = null;
			return false;
		}

		public UserResourceType createUserResourceType(string fullName) {
			UserResourceType type;
			if (dict.TryGetValue(fullName, out type))
				return type;

			var newFullName = getRealTypeFullName(fullName);
			type = new UserResourceType(newFullName, ResourceTypeCode.UserTypes + dict.Count);
			dict[fullName] = type;
			dict[newFullName] = type;
			return type;
		}

		static void splitTypeFullName(string fullName, out string typeName, out string assemblyName) {
			int index = fullName.IndexOf(',');
			if (index < 0) {
				typeName = fullName;
				assemblyName = null;
			}
			else {
				typeName = fullName.Substring(0, index);
				assemblyName = fullName.Substring(index + 1).Trim();
			}
		}

		string getRealTypeFullName(string fullName) {
			var newFullName = fullName;

			string typeName, assemblyName;
			splitTypeFullName(fullName, out typeName, out assemblyName);
			if (!string.IsNullOrEmpty(assemblyName))
				assemblyName = getRealAssemblyName(assemblyName);
			if (!string.IsNullOrEmpty(assemblyName))
				newFullName = string.Format("{0}, {1}", typeName, assemblyName);

			return newFullName;
		}

		string getRealAssemblyName(string assemblyName) {
			string newAsmName;
			if (!asmNameToAsmFullName.TryGetValue(assemblyName, out newAsmName))
				asmNameToAsmFullName[assemblyName] = newAsmName = tryGetRealAssemblyName(assemblyName);
			return newAsmName;
		}

		string tryGetRealAssemblyName(string assemblyName) {
			var simpleName = Utils.getAssemblySimpleName(assemblyName);

			foreach (var asmRef in module.AssemblyReferences) {
				if (asmRef.Name == simpleName)
					return asmRef.FullName;
			}

			try {
				return AssemblyResolver.Instance.Resolve(simpleName).FullName;
			}
			catch (ResolutionException) {
			}
			catch (AssemblyResolutionException) {
			}
			return null;
		}

		public List<UserResourceType> getSortedTypes() {
			var list = new List<UserResourceType>(dict.Values);
			list.Sort((a, b) => Utils.compareInt32((int)a.Code, (int)b.Code));
			return list;
		}
	}
}
