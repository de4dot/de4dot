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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeFort {
	class AssemblyDecrypter {
		ModuleDefMD module;
		EmbeddedResource assemblyEncryptedResource;
		PasswordInfo embedPassword;
		MethodDef embedInitMethod;
		MethodDef embedResolverMethod;

		public class AssemblyInfo {
			public readonly byte[] data;
			public readonly EmbeddedResource resource;
			public readonly string asmFullName;
			public readonly string asmSimpleName;
			public readonly string extension;

			public AssemblyInfo(byte[] data, EmbeddedResource resource, string asmFullName, string asmSimpleName, string extension) {
				this.data = data;
				this.resource = resource;
				this.asmFullName = asmFullName;
				this.asmSimpleName = asmSimpleName;
				this.extension = extension;
			}

			public override string ToString() {
				return asmFullName;
			}
		}

		public bool EncryptedDetected {
			get { return assemblyEncryptedResource != null; }
		}

		public bool MainAssemblyHasAssemblyResolver {
			get { return embedInitMethod != null; }
		}

		public bool Detected {
			get { return EncryptedDetected || MainAssemblyHasAssemblyResolver; }
		}

		public TypeDef Type {
			get { return embedInitMethod != null ? embedInitMethod.DeclaringType : null; }
		}

		public MethodDef InitMethod {
			get { return embedInitMethod; }
		}

		public AssemblyDecrypter(ModuleDefMD module) {
			this.module = module;
		}

		public AssemblyDecrypter(ModuleDefMD module, AssemblyDecrypter oldOne) {
			this.module = module;
			this.embedPassword = oldOne.embedPassword;
		}

		public void Find() {
			if (FindEncrypted())
				return;
			FindEmbedded();
		}

		static readonly string[] encryptedRequiredLocals = new string[] {
			"System.Byte",
			"System.Byte[]",
			"System.Int32",
			"System.IO.BinaryReader",
			"System.IO.MemoryStream",
			"System.IO.Stream",
			"System.Object[]",
			"System.Reflection.Assembly",
			"System.Type[]",
		};
		bool FindEncrypted() {
			var ep = module.EntryPoint;
			if (ep == null || ep.Body == null)
				return false;
			if (!DotNetUtils.IsMethod(ep, "System.Void", "(System.String[])"))
				return false;
			var initMethod = CheckCalledMethods(ep);
			if (initMethod == null || !new LocalTypes(initMethod).All(encryptedRequiredLocals))
				return false;
			var resource = GetResource();
			if (resource == null)
				return false;

			assemblyEncryptedResource = resource;
			return true;
		}

		MethodDef CheckCalledMethods(MethodDef method) {
			int calls = 0;
			TypeDef type = null;
			MethodDef initMethod = null;
			foreach (var calledMethod in DotNetUtils.GetCalledMethods(module, method)) {
				calls++;
				if (type != null && calledMethod.DeclaringType != type)
					return null;
				type = calledMethod.DeclaringType;
				if (initMethod == null)
					initMethod = calledMethod;
			}
			if (calls != 2)
				return null;
			return initMethod;
		}

		EmbeddedResource GetResource() {
			return DotNetUtils.GetResource(module, "_") as EmbeddedResource;
		}

		bool FindEmbedded() {
			return FindEmbedded(DotNetUtils.GetModuleTypeCctor(module)) ||
				FindEmbedded(module.EntryPoint);
		}

		bool FindEmbedded(MethodDef method) {
			if (method == null || method.Body == null)
				return false;
			foreach (var calledMethod in DotNetUtils.GetCalledMethods(module, method)) {
				var resolver = CheckInitMethod(calledMethod);
				if (resolver == null)
					continue;
				if (!CheckType(calledMethod.DeclaringType))
					continue;

				embedInitMethod = calledMethod;
				embedResolverMethod = resolver;
				return true;
			}

			return false;
		}

		MethodDef CheckInitMethod(MethodDef method) {
			if (method == null || !method.IsStatic || method.Body == null)
				return null;
			if (!DotNetUtils.IsMethod(method, "System.Void", "()"))
				return null;

			var resolver = DeobUtils.GetResolveMethod(method);
			if (resolver == null || resolver.DeclaringType != method.DeclaringType)
				return null;

			return resolver;
		}

		bool CheckType(TypeDef type) {
			if (DotNetUtils.GetMethod(type, "System.Byte[]", "(System.Byte[],System.String,System.String,System.Int32,System.String,System.Int32)") == null)
				return false;
			if (DotNetUtils.GetMethod(type, "System.String", "(System.String)") == null)
				return false;
			if (DotNetUtils.GetMethod(type, "System.Byte[]", "(System.Reflection.Assembly,System.String)") == null)
				return false;
			if (DotNetUtils.GetMethod(type, "System.Void", "(System.IO.Stream,System.IO.Stream)") == null)
				return false;

			return true;
		}

		public byte[] Decrypt() {
			if (assemblyEncryptedResource == null)
				return null;

			assemblyEncryptedResource.Data.Position = 0;
			var reader = new BinaryReader(assemblyEncryptedResource.Data.CreateStream());
			var encryptedData = DeobUtils.Gunzip(reader.BaseStream, reader.ReadInt32());
			reader = new BinaryReader(new MemoryStream(encryptedData));
			var serializedData = reader.ReadBytes(reader.ReadInt32());
			for (int i = 0; i < serializedData.Length; i++)
				serializedData[i] ^= 0xAD;
			var encryptedAssembly = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));

			var passwordFinder = new PasswordFinder(serializedData);
			PasswordInfo mainAsmPassword;
			passwordFinder.Find(out mainAsmPassword, out embedPassword);

			return Decrypt(mainAsmPassword, encryptedAssembly);
		}

		static byte[] Decrypt(PasswordInfo password, byte[] data) {
			const int iterations = 2;
			const int numBits = 0x100;
			var key = new Rfc2898DeriveBytes(password.passphrase, Encoding.UTF8.GetBytes(password.salt), iterations).GetBytes(numBits / 8);
			return DeobUtils.AesDecrypt(data, key, Encoding.UTF8.GetBytes(password.iv));
		}

		static byte[] Gunzip(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(data));
			return DeobUtils.Gunzip(reader.BaseStream, reader.ReadInt32());
		}

		public List<AssemblyInfo> GetAssemblyInfos(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			var infos = new List<AssemblyInfo>();

			if (embedResolverMethod != null) {
				simpleDeobfuscator.Deobfuscate(embedResolverMethod);
				simpleDeobfuscator.DecryptStrings(embedResolverMethod, deob);
				embedPassword = GetEmbedPassword(embedResolverMethod);
			}

			if (embedPassword == null)
				return infos;

			foreach (var rsrc in module.Resources) {
				var resource = rsrc as EmbeddedResource;
				if (resource == null)
					continue;
				if (!Regex.IsMatch(resource.Name.String, "^cfd_([0-9a-f]{2})+_$"))
					continue;

				var asmData = Decrypt(embedPassword, Gunzip(resource.Data.ReadAllBytes()));
				var mod = ModuleDefMD.Load(asmData);
				infos.Add(new AssemblyInfo(asmData, resource, mod.Assembly.FullName, mod.Assembly.Name.String, DeobUtils.GetExtension(mod.Kind)));
			}

			return infos;
		}

		static PasswordInfo GetEmbedPassword(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				int index = i;

				var ldstr1 = instrs[index++];
				if (ldstr1.OpCode.Code != Code.Ldstr)
					continue;
				var passphrase = GetString(ldstr1, instrs, ref index);

				var ldstr2 = instrs[index++];
				if (ldstr2.OpCode.Code != Code.Ldstr)
					continue;
				var salt = GetString(ldstr2, instrs, ref index);

				var ldci4 = instrs[index++];
				if (!ldci4.IsLdcI4())
					continue;

				var ldstr3 = instrs[index++];
				if (ldstr3.OpCode.Code != Code.Ldstr)
					continue;
				var iv = GetString(ldstr3, instrs, ref index);

				return new PasswordInfo(passphrase, salt, iv);
			}

			return null;
		}

		static string GetString(Instruction ldstr, IList<Instruction> instrs, ref int index) {
			var s = (string)ldstr.Operand;
			if (index >= instrs.Count)
				return s;
			var call = instrs[index];
			if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
				return s;
			index++;
			var calledMethod = call.Operand as IMethod;
			if (calledMethod.Name.String == "ToUpper")
				return s.ToUpper();
			if (calledMethod.Name.String == "ToLower")
				return s.ToLower();
			throw new ApplicationException(string.Format("Unknown method {0}", calledMethod));
		}
	}
}
