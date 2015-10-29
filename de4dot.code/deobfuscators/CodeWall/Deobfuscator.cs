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

namespace de4dot.code.deobfuscators.CodeWall {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "CodeWall";
		public const string THE_TYPE = "cw";
		const string DEFAULT_REGEX = @"!^[0-9A-F]{32}$&!^[_<>{}$.`-]$&" + DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		BoolOption dumpEmbeddedAssemblies;
		BoolOption decryptMainAsm;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			dumpEmbeddedAssemblies = new BoolOption(null, MakeArgName("embedded"), "Dump embedded assemblies", true);
			decryptMainAsm = new BoolOption(null, MakeArgName("decrypt-main"), "Decrypt main embedded assembly", true);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator CreateDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.Get(),
				DumpEmbeddedAssemblies = dumpEmbeddedAssemblies.Get(),
				DecryptMainAsm = decryptMainAsm.Get(),
			});
		}

		protected override IEnumerable<Option> GetOptionsInternal() {
			return new List<Option>() {
				dumpEmbeddedAssemblies,
				decryptMainAsm,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		MethodsDecrypter methodsDecrypter;
		StringDecrypter stringDecrypter;
		AssemblyDecrypter assemblyDecrypter;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;

		internal class Options : OptionsBase {
			public bool DumpEmbeddedAssemblies { get; set; }
			public bool DecryptMainAsm { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(methodsDecrypter.Detected) +
					ToInt32(stringDecrypter.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			// If methods are encrypted, and more than one obfuscator has been used, then CW
			// was most likely the last obfuscator used. Increment val so the user doesn't have
			// to force CW.
			if (methodsDecrypter.Detected)
				val += 50;

			return val;
		}

		protected override void ScanForObfuscator() {
			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.Find();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.Find();
			var version = DetectVersion();
			if (version != null)
				obfuscatorName = DeobfuscatorInfo.THE_NAME + " " + version;
		}

		string DetectVersion() {
			if (stringDecrypter.Detected) {
				switch (stringDecrypter.TheVersion) {
				case StringDecrypter.Version.V30: return "v3.0 - v3.5";
				case StringDecrypter.Version.V36: return "v3.6 - v4.1";
				}
			}

			if (methodsDecrypter.Detected)
				return "v3.0 - v4.1";

			return null;
		}

		[Flags]
		enum DecryptState {
			CanDecryptMethods = 1,
			CanGetMainAssembly = 2,
		}
		DecryptState decryptState = DecryptState.CanDecryptMethods | DecryptState.CanGetMainAssembly;
		public override bool GetDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if ((decryptState & DecryptState.CanDecryptMethods) != 0) {
				if (DecryptModule(ref newFileData, ref dumpedMethods)) {
					ModuleBytes = newFileData;
					decryptState &= ~DecryptState.CanDecryptMethods;
					return true;
				}
			}

			if (options.DecryptMainAsm && (decryptState & DecryptState.CanGetMainAssembly) != 0) {
				newFileData = GetMainAssemblyBytes();
				if (newFileData != null) {
					ModuleBytes = newFileData;
					decryptState &= ~DecryptState.CanGetMainAssembly;
					decryptState |= DecryptState.CanDecryptMethods;
					return true;
				}
			}

			return false;
		}

		bool DecryptModule(ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (!methodsDecrypter.Detected)
				return false;

			byte[] fileData = ModuleBytes ?? DeobUtils.ReadModule(module);
			using (var peImage = new MyPEImage(fileData)) {
				if (!methodsDecrypter.Decrypt(peImage, ref dumpedMethods))
					return false;
			}

			newFileData = fileData;
			return true;
		}

		byte[] GetMainAssemblyBytes() {
			try {
				InitializeStringDecrypter();
				InitializeAssemblyDecrypter();
			}
			catch {
				return null;
			}

			var asm = module.Assembly;
			if (asm == null || assemblyDecrypter == null)
				return null;
			var asmInfo = assemblyDecrypter.FindMain(asm.FullName) ?? assemblyDecrypter.FindMain();
			if (asmInfo == null)
				return null;

			assemblyDecrypter.Remove(asmInfo);
			return asmInfo.data;
		}

		public override IDeobfuscator ModuleReloaded(ModuleDefMD module) {
			var newOne = new Deobfuscator(options);
			newOne.SetModule(module);
			newOne.methodsDecrypter = new MethodsDecrypter(module);
			newOne.methodsDecrypter.Find();
			newOne.stringDecrypter = new StringDecrypter(module);
			newOne.stringDecrypter.Find();
			newOne.assemblyDecrypter = assemblyDecrypter;
			newOne.ModuleBytes = ModuleBytes;
			newOne.decryptState = decryptState;
			return newOne;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			InitializeStringDecrypter();
			InitializeAssemblyDecrypter();
			DumpEmbeddedAssemblies();
		}

		bool hasInitializedStringDecrypter = false;
		void InitializeStringDecrypter() {
			if (hasInitializedStringDecrypter)
				return;
			stringDecrypter.Initialize(DeobfuscatedFile);
			foreach (var info in stringDecrypter.Infos)
				staticStringInliner.Add(info.Method, (method, gim, args) => stringDecrypter.Decrypt(method, (int)args[0], (int)args[1], (int)args[2]));
			DeobfuscatedFile.StringDecryptersAdded();
			hasInitializedStringDecrypter = true;
		}

		void InitializeAssemblyDecrypter() {
			if (!options.DumpEmbeddedAssemblies || assemblyDecrypter != null)
				return;
			assemblyDecrypter = new AssemblyDecrypter(module, DeobfuscatedFile, this);
			assemblyDecrypter.Find();
		}

		void DumpEmbeddedAssemblies() {
			if (assemblyDecrypter == null)
				return;
			foreach (var info in assemblyDecrypter.AssemblyInfos) {
				var asmName = info.assemblySimpleName;
				if (info.isEntryPointAssembly)
					asmName += "_real";
				DeobfuscatedFile.CreateAssemblyFile(info.data, asmName, info.extension);
			}
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			methodsDecrypter.Deobfuscate(blocks);
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			if (CanRemoveStringDecrypterType) {
				foreach (var info in stringDecrypter.Infos) {
					AddResourceToBeRemoved(info.Resource, "Encrypted strings");
					AddTypeToBeRemoved(info.Type, "String decrypter type");
				}
			}
			base.DeobfuscateEnd();
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var info in stringDecrypter.Infos)
				list.Add(info.Method.MDToken.ToInt32());
			return list;
		}
	}
}
