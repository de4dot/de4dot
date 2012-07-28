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
using Mono.Cecil;
using Mono.MyStuff;
using de4dot.blocks;
using de4dot.PE;

namespace de4dot.code.deobfuscators.CodeWall {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "CodeWall";
		public const string THE_TYPE = "cw";
		const string DEFAULT_REGEX = @"!^[0-9A-F]{32}$&!^[_<>{}$.`-]$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		BoolOption dumpEmbeddedAssemblies;
		BoolOption decryptMainAsm;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			dumpEmbeddedAssemblies = new BoolOption(null, makeArgName("embedded"), "Dump embedded assemblies", true);
			decryptMainAsm = new BoolOption(null, makeArgName("decrypt-main"), "Decrypt main embedded assembly", true);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
				DumpEmbeddedAssemblies = dumpEmbeddedAssemblies.get(),
				DecryptMainAsm = decryptMainAsm.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
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

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(methodsDecrypter.Detected) +
					toInt32(stringDecrypter.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			// If methods are encrypted, and more than one obfuscator has been used, then CW
			// was most likely the last obfuscator used. Increment val so the user doesn't have
			// to force CW.
			if (methodsDecrypter.Detected)
				val += 50;

			return val;
		}

		protected override void scanForObfuscator() {
			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.find();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find();
			var version = detectVersion();
			if (version != null)
				obfuscatorName = DeobfuscatorInfo.THE_NAME + " " + version;
		}

		string detectVersion() {
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
		public override bool getDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if ((decryptState & DecryptState.CanDecryptMethods) != 0) {
				if (decryptModule(ref newFileData, ref dumpedMethods)) {
					ModuleBytes = newFileData;
					decryptState &= ~DecryptState.CanDecryptMethods;
					return true;
				}
			}

			if (options.DecryptMainAsm && (decryptState & DecryptState.CanGetMainAssembly) != 0) {
				newFileData = getMainAssemblyBytes();
				if (newFileData != null) {
					ModuleBytes = newFileData;
					decryptState &= ~DecryptState.CanGetMainAssembly;
					decryptState |= DecryptState.CanDecryptMethods;
					return true;
				}
			}

			return false;
		}

		bool decryptModule(ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (!methodsDecrypter.Detected)
				return false;

			byte[] fileData = ModuleBytes ?? DeobUtils.readModule(module);
			var peImage = new PeImage(fileData);

			if (!methodsDecrypter.decrypt(peImage, ref dumpedMethods))
				return false;

			newFileData = fileData;
			return true;
		}

		byte[] getMainAssemblyBytes() {
			try {
				initializeStringDecrypter();
				initializeAssemblyDecrypter();
			}
			catch {
				return null;
			}

			var asm = module.Assembly;
			if (asm == null || assemblyDecrypter == null)
				return null;
			var asmInfo = assemblyDecrypter.findMain(asm.FullName) ?? assemblyDecrypter.findMain();
			if (asmInfo == null)
				return null;

			assemblyDecrypter.remove(asmInfo);
			return asmInfo.data;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.methodsDecrypter = new MethodsDecrypter(module);
			newOne.methodsDecrypter.find();
			newOne.stringDecrypter = new StringDecrypter(module);
			newOne.stringDecrypter.find();
			newOne.assemblyDecrypter = assemblyDecrypter;
			newOne.ModuleBytes = ModuleBytes;
			newOne.decryptState = decryptState;
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();
			addAssemblyReferenceToBeRemoved(methodsDecrypter.AssemblyNameReference, "Obfuscator decrypter DLL reference");

			initializeStringDecrypter();
			initializeAssemblyDecrypter();
			dumpEmbeddedAssemblies();
		}

		bool hasInitializedStringDecrypter = false;
		void initializeStringDecrypter() {
			if (hasInitializedStringDecrypter)
				return;
			stringDecrypter.initialize(DeobfuscatedFile);
			foreach (var info in stringDecrypter.Infos)
				staticStringInliner.add(info.Method, (method, gim, args) => stringDecrypter.decrypt(method, (int)args[0], (int)args[1], (int)args[2]));
			DeobfuscatedFile.stringDecryptersAdded();
			hasInitializedStringDecrypter = true;
		}

		void initializeAssemblyDecrypter() {
			if (!options.DumpEmbeddedAssemblies || assemblyDecrypter != null)
				return;
			assemblyDecrypter = new AssemblyDecrypter(module, DeobfuscatedFile, this);
			assemblyDecrypter.find();
		}

		void dumpEmbeddedAssemblies() {
			if (assemblyDecrypter == null)
				return;
			foreach (var info in assemblyDecrypter.AssemblyInfos) {
				var asmName = info.assemblySimpleName;
				if (info.isEntryPointAssembly)
					asmName += "_real";
				DeobfuscatedFile.createAssemblyFile(info.data, asmName, info.extension);
			}
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			methodsDecrypter.deobfuscate(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			if (CanRemoveStringDecrypterType) {
				foreach (var info in stringDecrypter.Infos) {
					addResourceToBeRemoved(info.Resource, "Encrypted strings");
					addTypeToBeRemoved(info.Type, "String decrypter type");
				}
			}
			base.deobfuscateEnd();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var info in stringDecrypter.Infos)
				list.Add(info.Method.MetadataToken.ToInt32());
			return list;
		}
	}
}
