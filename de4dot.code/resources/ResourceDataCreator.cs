/*
    Copyright (C) 2011-2014 de4dot@gmail.com

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
using dnlib.DotNet;

namespace de4dot.code.resources {
	class ResourceDataCreator {
		readonly ModuleDef module;
		readonly ModuleDefMD moduleMD;
		readonly Dictionary<string, UserResourceType> dict = new Dictionary<string, UserResourceType>(StringComparer.Ordinal);
		readonly Dictionary<string, string> asmNameToAsmFullName = new Dictionary<string, string>(StringComparer.Ordinal);

		public ResourceDataCreator(ModuleDef module) {
			this.module = module;
			this.moduleMD = module as ModuleDefMD;
		}

		public int Count {
			get { return dict.Count; }
		}

		public BuiltInResourceData CreateNull() {
			return new BuiltInResourceData(ResourceTypeCode.Null, null);
		}

		public BuiltInResourceData Create(string value) {
			return new BuiltInResourceData(ResourceTypeCode.String, value);
		}

		public BuiltInResourceData Create(bool value) {
			return new BuiltInResourceData(ResourceTypeCode.Boolean, value);
		}

		public BuiltInResourceData Create(char value) {
			return new BuiltInResourceData(ResourceTypeCode.Char, value);
		}

		public BuiltInResourceData Create(byte value) {
			return new BuiltInResourceData(ResourceTypeCode.Byte, value);
		}

		public BuiltInResourceData Create(sbyte value) {
			return new BuiltInResourceData(ResourceTypeCode.SByte, value);
		}

		public BuiltInResourceData Create(short value) {
			return new BuiltInResourceData(ResourceTypeCode.Int16, value);
		}

		public BuiltInResourceData Create(ushort value) {
			return new BuiltInResourceData(ResourceTypeCode.UInt16, value);
		}

		public BuiltInResourceData Create(int value) {
			return new BuiltInResourceData(ResourceTypeCode.Int32, value);
		}

		public BuiltInResourceData Create(uint value) {
			return new BuiltInResourceData(ResourceTypeCode.UInt32, value);
		}

		public BuiltInResourceData Create(long value) {
			return new BuiltInResourceData(ResourceTypeCode.Int64, value);
		}

		public BuiltInResourceData Create(ulong value) {
			return new BuiltInResourceData(ResourceTypeCode.UInt64, value);
		}

		public BuiltInResourceData Create(float value) {
			return new BuiltInResourceData(ResourceTypeCode.Single, value);
		}

		public BuiltInResourceData Create(double value) {
			return new BuiltInResourceData(ResourceTypeCode.Double, value);
		}

		public BuiltInResourceData Create(decimal value) {
			return new BuiltInResourceData(ResourceTypeCode.Decimal, value);
		}

		public BuiltInResourceData Create(DateTime value) {
			return new BuiltInResourceData(ResourceTypeCode.DateTime, value);
		}

		public BuiltInResourceData Create(TimeSpan value) {
			return new BuiltInResourceData(ResourceTypeCode.TimeSpan, value);
		}

		public BuiltInResourceData Create(byte[] value) {
			return new BuiltInResourceData(ResourceTypeCode.ByteArray, value);
		}

		public BuiltInResourceData CreateStream(byte[] value) {
			return new BuiltInResourceData(ResourceTypeCode.Stream, value);
		}

		public CharArrayResourceData Create(char[] value) {
			return new CharArrayResourceData(CreateUserResourceType(CharArrayResourceData.ReflectionTypeName), value);
		}

		public IconResourceData CreateIcon(byte[] value) {
			return new IconResourceData(CreateUserResourceType(IconResourceData.ReflectionTypeName), value);
		}

		public ImageResourceData CreateImage(byte[] value) {
			return new ImageResourceData(CreateUserResourceType(ImageResourceData.ReflectionTypeName), value);
		}

		public BinaryResourceData CreateSerialized(byte[] value) {
			string assemblyName, typeName;
			if (!GetSerializedTypeAndAssemblyName(value, out assemblyName, out typeName))
				throw new ApplicationException("Could not get serialized type name");
			string fullName = string.Format("{0},{1}", typeName, assemblyName);
			return new BinaryResourceData(CreateUserResourceType(fullName), value);
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

		bool GetSerializedTypeAndAssemblyName(byte[] value, out string assemblyName, out string typeName) {
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

		public UserResourceType CreateUserResourceType(string fullName) {
			UserResourceType type;
			if (dict.TryGetValue(fullName, out type))
				return type;

			var newFullName = GetRealTypeFullName(fullName);
			type = new UserResourceType(newFullName, ResourceTypeCode.UserTypes + dict.Count);
			dict[fullName] = type;
			dict[newFullName] = type;
			return type;
		}

		static void SplitTypeFullName(string fullName, out string typeName, out string assemblyName) {
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

		string GetRealTypeFullName(string fullName) {
			var newFullName = fullName;

			string typeName, assemblyName;
			SplitTypeFullName(fullName, out typeName, out assemblyName);
			if (!string.IsNullOrEmpty(assemblyName))
				assemblyName = GetRealAssemblyName(assemblyName);
			if (!string.IsNullOrEmpty(assemblyName))
				newFullName = string.Format("{0}, {1}", typeName, assemblyName);

			return newFullName;
		}

		string GetRealAssemblyName(string assemblyName) {
			string newAsmName;
			if (!asmNameToAsmFullName.TryGetValue(assemblyName, out newAsmName))
				asmNameToAsmFullName[assemblyName] = newAsmName = TryGetRealAssemblyName(assemblyName);
			return newAsmName;
		}

		string TryGetRealAssemblyName(string assemblyName) {
			var simpleName = Utils.GetAssemblySimpleName(assemblyName);

			if (moduleMD != null) {
				var asmRef = moduleMD.GetAssemblyRef(simpleName);
				if (asmRef != null)
					return asmRef.FullName;
			}

			var asm = TheAssemblyResolver.Instance.Resolve(new AssemblyNameInfo(simpleName), module);
			return asm == null ? null : asm.FullName;
		}

		public List<UserResourceType> GetSortedTypes() {
			var list = new List<UserResourceType>(dict.Values);
			list.Sort((a, b) => ((int)a.Code).CompareTo((int)b.Code));
			return list;
		}
	}
}
