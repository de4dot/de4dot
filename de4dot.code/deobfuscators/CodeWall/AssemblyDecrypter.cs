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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;
using dnlib.DotNet.Resources;

namespace de4dot.code.deobfuscators.CodeWall {
	class AssemblyDecrypter {
		ModuleDefMD module;
		ISimpleDeobfuscator simpleDeobfuscator;
		IDeobfuscator deob;
		List<AssemblyInfo> assemblyInfos = new List<AssemblyInfo>();
		string entryPointAssemblyKey;
		string resourcePassword;
		string resourceSalt;
		EmbeddedResource assemblyResource;
		ModuleDefMD resourceModule;

		public class AssemblyInfo {
			public readonly byte[] data;
			public readonly string extension;
			public readonly string assemblyFullName;
			public readonly string assemblySimpleName;
			public readonly bool isEntryPointAssembly;

			public AssemblyInfo(byte[] data, string extension, string assemblyFullName, string assemblySimpleName, bool isEntryPointAssembly) {
				this.data = data;
				this.extension = extension;
				this.assemblyFullName = assemblyFullName;
				this.assemblySimpleName = assemblySimpleName;
				this.isEntryPointAssembly = isEntryPointAssembly;
			}

			public override string ToString() {
				return assemblyFullName;
			}
		}

		public IEnumerable<AssemblyInfo> AssemblyInfos {
			get { return assemblyInfos; }
		}

		public AssemblyDecrypter(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
			this.deob = deob;
		}

		public void Find() {
			var method = module.EntryPoint;
			if (!CheckEntryPoint(method))
				return;

			MethodDef decryptAssemblyMethod;
			var mainKey = GetMainResourceKey(method, out decryptAssemblyMethod);
			if (mainKey == null)
				return;

			DeobfuscateAll(decryptAssemblyMethod);
			ModuleDefMD theResourceModule;
			var resource = GetResource(decryptAssemblyMethod, out theResourceModule);
			if (resource == null)
				return;
			string password, salt;
			if (!GetPassword(decryptAssemblyMethod, out password, out salt))
				return;

			entryPointAssemblyKey = mainKey;
			resourcePassword = password;
			resourceSalt = salt;
			assemblyResource = resource;
			resourceModule = theResourceModule;
			DecryptAllAssemblies();
		}

		static readonly string[] requiredLocals = new string[] {
			"System.AppDomain",
			"System.DateTime",
		};
		bool CheckEntryPoint(MethodDef method) {
			if (method == null)
				return false;
			if (!new LocalTypes(method).All(requiredLocals))
				return false;
			var handlers = DeobUtils.GetAllResolveHandlers(method);
			if (handlers.Count != 1)
				return false;

			return true;
		}

		void DeobfuscateAll(MethodDef method) {
			simpleDeobfuscator.Deobfuscate(method);
			simpleDeobfuscator.DecryptStrings(method, deob);
		}

		string GetMainResourceKey(MethodDef method, out MethodDef decryptAssemblyMethod) {
			foreach (var calledMethod in DotNetUtils.GetCalledMethods(module, method)) {
				if (!calledMethod.IsStatic || calledMethod.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Void", "(System.String[])"))
					continue;

				DeobfuscateAll(calledMethod);
				string keyInfo = GetMainResourceKeyInfo(calledMethod, out decryptAssemblyMethod);
				if (keyInfo == null)
					continue;
				return BitConverter.ToString(new MD5CryptoServiceProvider().ComputeHash(new ASCIIEncoding().GetBytes(keyInfo))).Replace("-", "");
			}

			decryptAssemblyMethod = null;
			return null;
		}

		string GetMainResourceKeyInfo(MethodDef method, out MethodDef decryptAssemblyMethod) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldstr = instrs[i];
				if (ldstr.OpCode.Code != Code.Ldstr)
					continue;
				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDef;
				if (calledMethod == null)
					continue;

				decryptAssemblyMethod = calledMethod;
				return (string)ldstr.Operand;
			}
			decryptAssemblyMethod = null;
			return null;
		}

		EmbeddedResource GetResource(MethodDef method, out ModuleDefMD theResourceModule) {
			string resourceDllFileName = null;
			theResourceModule = module;
			foreach (var s in DotNetUtils.GetCodeStrings(method)) {
				if (s.Length > 0 && s[0] == '\\')
					resourceDllFileName = s;
				var resource = DotNetUtils.GetResource(theResourceModule, s + ".resources") as EmbeddedResource;
				if (resource != null)
					return resource;
			}

			if (resourceDllFileName == null)
				return null;
			// Here if CW 2.x
			theResourceModule = GetResourceModule(resourceDllFileName);
			if (theResourceModule == null)
				return null;
			foreach (var s in DotNetUtils.GetCodeStrings(method)) {
				var resource = DotNetUtils.GetResource(theResourceModule, s + ".resources") as EmbeddedResource;
				if (resource != null)
					return resource;
			}

			theResourceModule = null;
			return null;
		}

		ModuleDefMD GetResourceModule(string name) {
			try {
				var resourceDllFileName = Path.Combine(Path.GetDirectoryName(module.Location), name.Substring(1));
				return ModuleDefMD.Load(resourceDllFileName);
			}
			catch {
				return null;
			}
		}

		bool GetPassword(MethodDef method, out string password, out string salt) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldstr1 = instrs[i];
				if (ldstr1.OpCode.Code != Code.Ldstr)
					continue;
				var ldstr2 = instrs[i + 1];
				if (ldstr2.OpCode.Code != Code.Ldstr)
					continue;

				password = (string)ldstr1.Operand;
				salt = (string)ldstr2.Operand;
				if (password == null || salt == null)
					continue;
				return true;
			}

			password = null;
			salt = null;
			return false;
		}

		void DecryptAllAssemblies() {
			if (assemblyResource == null)
				return;
			assemblyResource.Data.Position = 0;
			var resourceSet = ResourceReader.Read(resourceModule, assemblyResource.Data);
			foreach (var resourceElement in resourceSet.ResourceElements) {
				if (resourceElement.ResourceData.Code != ResourceTypeCode.ByteArray)
					throw new ApplicationException("Invalid resource");
				var resourceData = (BuiltInResourceData)resourceElement.ResourceData;
				var assemblyData = Decrypt((byte[])resourceData.Data);
				var theModule = ModuleDefMD.Load(assemblyData);
				bool isMain = resourceElement.Name == entryPointAssemblyKey;
				assemblyInfos.Add(new AssemblyInfo(assemblyData, DeobUtils.GetExtension(theModule.Kind), theModule.Assembly.FullName, theModule.Assembly.Name.String, isMain));
			}
		}

		byte[] Decrypt(byte[] encrypted) {
			var keyGenerator = new PasswordDeriveBytes(resourcePassword, Encoding.ASCII.GetBytes(resourceSalt));
			return DeobUtils.Inflate(DeobUtils.AesDecrypt(encrypted, keyGenerator.GetBytes(32), keyGenerator.GetBytes(16)), false);
		}

		public AssemblyInfo FindMain(string asmFullName) {
			foreach (var asmInfo in assemblyInfos) {
				if (asmInfo.isEntryPointAssembly && asmInfo.assemblyFullName == asmFullName)
					return asmInfo;
			}
			return null;
		}

		public AssemblyInfo FindMain() {
			foreach (var asmInfo in assemblyInfos) {
				if (asmInfo.isEntryPointAssembly)
					return asmInfo;
			}
			return null;
		}

		public void Remove(AssemblyInfo asmInfo) {
			assemblyInfos.Remove(asmInfo);
		}
	}
}
