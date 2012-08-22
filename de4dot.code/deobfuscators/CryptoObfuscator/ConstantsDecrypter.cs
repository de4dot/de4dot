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
using System.Text;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class ConstantsDecrypter {
		ModuleDefinition module;
		TypeDefinition decrypterType;
		MethodDefinition methodI4;
		MethodDefinition methodI8;
		MethodDefinition methodR4;
		MethodDefinition methodR8;
		EmbeddedResource encryptedResource;
		byte[] constantsData;

		public TypeDefinition Type {
			get { return decrypterType; }
		}

		public EmbeddedResource Resource {
			get { return encryptedResource; }
		}

		public MethodDefinition Int32Decrypter {
			get { return methodI4; }
		}

		public MethodDefinition Int64Decrypter {
			get { return methodI8; }
		}

		public MethodDefinition SingleDecrypter {
			get { return methodR4; }
		}

		public MethodDefinition DoubleDecrypter {
			get { return methodR8; }
		}

		public bool Detected {
			get { return decrypterType != null; }
		}

		public ConstantsDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				if (!checkType(type))
					continue;

				decrypterType = type;
				return;
			}
		}

		static readonly string[] requiredTypes = new string[] {
			"System.Byte[]",
		};
		bool checkType(TypeDefinition type) {
			if (type.Methods.Count != 7)
				return false;
			if (type.Fields.Count < 1 || type.Fields.Count > 2)
				return false;
			if (!new FieldTypes(type).all(requiredTypes))
				return false;
			if (!checkMethods(type))
				return false;

			return true;
		}

		bool checkMethods(TypeDefinition type) {
			methodI4 = DotNetUtils.getMethod(type, "System.Int32", "(System.Int32)");
			methodI8 = DotNetUtils.getMethod(type, "System.Int64", "(System.Int32)");
			methodR4 = DotNetUtils.getMethod(type, "System.Single", "(System.Int32)");
			methodR8 = DotNetUtils.getMethod(type, "System.Double", "(System.Int32)");

			return methodI4 != null && methodI8 != null &&
				methodR4 != null && methodR8 != null;
		}

		public void init(ResourceDecrypter resourceDecrypter) {
			if (decrypterType == null)
				return;

			encryptedResource = CoUtils.getResource(module, DotNetUtils.getCodeStrings(DotNetUtils.getMethod(decrypterType, ".cctor")));
			constantsData = resourceDecrypter.decrypt(encryptedResource.GetResourceStream());
		}

		public int decryptInt32(int index) {
			return BitConverter.ToInt32(constantsData, index);
		}

		public long decryptInt64(int index) {
			return BitConverter.ToInt64(constantsData, index);
		}

		public float decryptSingle(int index) {
			return BitConverter.ToSingle(constantsData, index);
		}

		public double decryptDouble(int index) {
			return BitConverter.ToDouble(constantsData, index);
		}
	}
}
