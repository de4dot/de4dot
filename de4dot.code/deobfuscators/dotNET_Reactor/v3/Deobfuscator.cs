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
using System.Text.RegularExpressions;
using dnlib.IO;
using dnlib.PE;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v3 {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = ".NET Reactor";
		public const string THE_TYPE = "dr3";
		const string DEFAULT_REGEX = DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		BoolOption restoreTypes;
		BoolOption inlineMethods;
		BoolOption removeInlinedMethods;
		BoolOption removeNamespaces;
		BoolOption removeAntiStrongName;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			restoreTypes = new BoolOption(null, MakeArgName("types"), "Restore types (object -> real type)", true);
			inlineMethods = new BoolOption(null, MakeArgName("inline"), "Inline short methods", true);
			removeInlinedMethods = new BoolOption(null, MakeArgName("remove-inlined"), "Remove inlined methods", true);
			removeNamespaces = new BoolOption(null, MakeArgName("ns1"), "Clear namespace if there's only one class in it", true);
			removeAntiStrongName = new BoolOption(null, MakeArgName("sn"), "Remove anti strong name code", true);
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
				RestoreTypes = restoreTypes.Get(),
				InlineMethods = inlineMethods.Get(),
				RemoveInlinedMethods = removeInlinedMethods.Get(),
				RemoveNamespaces = removeNamespaces.Get(),
				RemoveAntiStrongName = removeAntiStrongName.Get(),
			});
		}

		protected override IEnumerable<Option> GetOptionsInternal() {
			return new List<Option>() {
				restoreTypes,
				inlineMethods,
				removeInlinedMethods,
				removeNamespaces,
				removeAntiStrongName,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;

		DecrypterType decrypterType;
		NativeLibSaver nativeLibSaver;
		AntiStrongName antiStrongName;
		LibAssemblyResolver libAssemblyResolver;
		List<UnpackedFile> unpackedFiles = new List<UnpackedFile>();

		bool unpackedNativeFile = false;
		bool canRemoveDecrypterType = true;
		bool startedDeobfuscating = false;

		internal class Options : OptionsBase {
			public bool RestoreTypes { get; set; }
			public bool InlineMethods { get; set; }
			public bool RemoveInlinedMethods { get; set; }
			public bool RemoveNamespaces { get; set; }
			public bool RemoveAntiStrongName { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME + " 3.x"; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		protected override bool CanInlineMethods {
			get { return startedDeobfuscating ? options.InlineMethods : true; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;

			if (options.RemoveNamespaces)
				this.RenamingOptions |= RenamingOptions.RemoveNamespaceIfOneType;
			else
				this.RenamingOptions &= ~RenamingOptions.RemoveNamespaceIfOneType;
		}

		public override byte[] UnpackNativeFile(IPEImage peImage) {
			var unpacker = new ApplicationModeUnpacker(peImage);
			var data = unpacker.Unpack();
			if (data == null)
				return null;

			unpackedFiles.AddRange(unpacker.EmbeddedAssemblies);
			unpackedNativeFile = true;
			ModuleBytes = data;
			return data;
		}

		bool NeedsPatching() {
			return decrypterType.LinkedResource != null || nativeLibSaver.Resource != null;
		}

		public override bool GetDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (count != 0 || !NeedsPatching())
				return false;

			var fileData = ModuleBytes ?? DeobUtils.ReadModule(module);
			if (!decrypterType.Patch(fileData))
				return false;

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator ModuleReloaded(ModuleDefMD module) {
			var newOne = new Deobfuscator(options);
			newOne.SetModule(module);
			newOne.decrypterType = new DecrypterType(module, decrypterType);
			newOne.nativeLibSaver = new NativeLibSaver(module, nativeLibSaver);
			return newOne;
		}

		public override void Initialize(ModuleDefMD module) {
			base.Initialize(module);
		}

		static Regex isRandomName = new Regex(@"^[A-Z]{30,40}$");
		static Regex isRandomNameMembers = new Regex(@"^[a-zA-Z0-9]{9,11}$");	// methods, fields, props, events
		static Regex isRandomNameTypes = new Regex(@"^[a-zA-Z0-9]{18,19}(?:`\d+)?$");	// types, namespaces

		bool CheckValidName(string name, Regex regex) {
			if (isRandomName.IsMatch(name))
				return false;
			if (regex.IsMatch(name)) {
				if (RandomNameChecker.IsRandom(name))
					return false;
				if (!RandomNameChecker.IsNonRandom(name))
					return false;
			}
			return CheckValidName(name);
		}

		public override bool IsValidNamespaceName(string ns) {
			if (ns == null)
				return false;
			if (ns.Contains("."))
				return base.IsValidNamespaceName(ns);
			return CheckValidName(ns, isRandomNameTypes);
		}

		public override bool IsValidTypeName(string name) {
			return name != null && CheckValidName(name, isRandomNameTypes);
		}

		public override bool IsValidMethodName(string name) {
			return name != null && CheckValidName(name, isRandomNameMembers);
		}

		public override bool IsValidPropertyName(string name) {
			return name != null && CheckValidName(name, isRandomNameMembers);
		}

		public override bool IsValidEventName(string name) {
			return name != null && CheckValidName(name, isRandomNameMembers);
		}

		public override bool IsValidFieldName(string name) {
			return name != null && CheckValidName(name, isRandomNameMembers);
		}

		public override bool IsValidGenericParamName(string name) {
			return name != null && CheckValidName(name, isRandomNameMembers);
		}

		public override bool IsValidMethodArgName(string name) {
			return name != null && CheckValidName(name, isRandomNameMembers);
		}

		public override bool IsValidMethodReturnArgName(string name) {
			return string.IsNullOrEmpty(name) || CheckValidName(name, isRandomNameMembers);
		}

		public override bool IsValidResourceKeyName(string name) {
			return name != null && CheckValidName(name, isRandomNameMembers);
		}

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(unpackedNativeFile) +
					ToInt32(decrypterType.Detected) +
					ToInt32(nativeLibSaver.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void ScanForObfuscator() {
			decrypterType = new DecrypterType(module);
			decrypterType.Find();
			nativeLibSaver = new NativeLibSaver(module);
			nativeLibSaver.Find();
			obfuscatorName = DetectVersion();
			if (unpackedNativeFile)
				obfuscatorName += " (native)";
		}

		string DetectVersion() {
			return DeobfuscatorInfo.THE_NAME + " 3.x";
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			antiStrongName = new AntiStrongName();

			staticStringInliner.Add(decrypterType.StringDecrypter1, (method2, gim, args) => {
				return decrypterType.Decrypt1((string)args[0]);
			});
			staticStringInliner.Add(decrypterType.StringDecrypter2, (method2, gim, args) => {
				return decrypterType.Decrypt2((string)args[0]);
			});
			DeobfuscatedFile.StringDecryptersAdded();

			libAssemblyResolver = new LibAssemblyResolver(module);
			libAssemblyResolver.Find(DeobfuscatedFile, this);

			if (Operations.DecryptStrings == OpDecryptString.None)
				canRemoveDecrypterType = false;

			RemoveInitCall(nativeLibSaver.InitMethod);
			AddResourceToBeRemoved(nativeLibSaver.Resource, "Native lib resource");
			AddTypeToBeRemoved(nativeLibSaver.Type, "Native lib saver type");

			foreach (var initMethod in decrypterType.InitMethods)
				RemoveInitCall(initMethod);

			DumpUnpackedFiles();
			DumpResourceFiles();

			startedDeobfuscating = true;
		}

		void RemoveInitCall(MethodDef initMethod) {
			AddCctorInitCallToBeRemoved(initMethod);
			AddCtorInitCallToBeRemoved(initMethod);
		}

		void DumpUnpackedFiles() {
			foreach (var unpackedFile in unpackedFiles)
				DeobfuscatedFile.CreateAssemblyFile(unpackedFile.data,
							Win32Path.GetFileNameWithoutExtension(unpackedFile.filename),
							Win32Path.GetExtension(unpackedFile.filename));
		}

		void DumpResourceFiles() {
			foreach (var resource in libAssemblyResolver.Resources) {
				var mod = ModuleDefMD.Load(resource.Data.ReadAllBytes());
				AddResourceToBeRemoved(resource, string.Format("Embedded assembly: {0}", mod.Assembly.FullName));
				DeobfuscatedFile.CreateAssemblyFile(resource.GetResourceData(),
							Utils.GetAssemblySimpleName(mod.Assembly.FullName),
							DeobUtils.GetExtension(mod.Kind));
			}
			RemoveInitCall(libAssemblyResolver.InitMethod);
			AddCallToBeRemoved(module.EntryPoint, libAssemblyResolver.InitMethod);
			AddTypeToBeRemoved(libAssemblyResolver.Type, "Assembly resolver type (library mode)");
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			if (options.RemoveAntiStrongName) {
				if (antiStrongName.Remove(blocks))
					Logger.v("Removed Anti Strong Name code");
			}
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			RemoveInlinedMethods();
			if (options.RestoreTypes)
				new TypesRestorer(module).Deobfuscate();

			if (canRemoveDecrypterType && !IsTypeCalled(decrypterType.Type)) {
				AddTypeToBeRemoved(decrypterType.Type, "Decrypter type");
				AddResourceToBeRemoved(decrypterType.LinkedResource, "Native lib linked resource");
			}

			base.DeobfuscateEnd();
		}

		void RemoveInlinedMethods() {
			if (!options.InlineMethods || !options.RemoveInlinedMethods)
				return;
			FindAndRemoveInlinedMethods();
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var method in decrypterType.StringDecrypters)
				list.Add(method.MDToken.ToInt32());
			return list;
		}
	}
}
