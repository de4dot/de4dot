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
using dnlib.DotNet.Emit;
using de4dot.blocks;
using de4dot.code.renamer;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Eazfuscator.NET";
		public const string THE_TYPE = "ef";
		const string DEFAULT_REGEX = @"!^[a-zA-Z]$&!^#=&!^dje_.+_ejd$&" + DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
		}

		public override string Name => THE_NAME;
		public override string Type => THE_TYPE;

		public override IDeobfuscator CreateDeobfuscator() =>
			new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.Get(),
			});
	}

	class Deobfuscator : DeobfuscatorBase {
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		bool detectedVersion = false;

		DecrypterType decrypterType;
		StringDecrypter stringDecrypter;
		AssemblyResolver assemblyResolver;
		ResourceResolver resourceResolver;
		ResourceMethodsRestorer resourceMethodsRestorer;

		internal class Options : OptionsBase {
		}

		public override string Type => DeobfuscatorInfo.THE_TYPE;
		public override string TypeLong => DeobfuscatorInfo.THE_NAME;
		public override string Name => obfuscatorName;
		public Deobfuscator(Options options) : base(options) { }

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(stringDecrypter.Detected) +
					ToInt32(assemblyResolver.Detected) +
					ToInt32(resourceResolver.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);
			if (detectedVersion)
				val += 10;

			return val;
		}

		protected override void ScanForObfuscator() {
			decrypterType = new DecrypterType(module, DeobfuscatedFile);
			stringDecrypter = new StringDecrypter(module, decrypterType);
			stringDecrypter.Find();
			assemblyResolver = new AssemblyResolver(module, decrypterType);
			assemblyResolver.Find();
			resourceResolver = new ResourceResolver(module, assemblyResolver);
			resourceResolver.Find();
			if (stringDecrypter.Detected)
				DetectVersion();
		}

		void DetectVersion() {
			var version = new VersionDetector(module, stringDecrypter).Detect();
			if (version == null)
				return;

			detectedVersion = true;
			obfuscatorName = DeobfuscatorInfo.THE_NAME + " " +  version;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			stringDecrypter.Initialize(DeobfuscatedFile);
			staticStringInliner.Add(stringDecrypter.RealMethod, (method2, gim, args) => {
				return stringDecrypter.Decrypt((int)args[0]);
			});
			DeobfuscatedFile.StringDecryptersAdded();

			assemblyResolver.Initialize(DeobfuscatedFile, this);
			assemblyResolver.InitializeEmbeddedFiles();
			AddModuleCctorInitCallToBeRemoved(assemblyResolver.InitMethod);

			resourceResolver.Initialize(DeobfuscatedFile, this);
			foreach (var info in resourceResolver.MergeResources())
				AddResourceToBeRemoved(info.Resource, "Encrypted resources");
			AddModuleCctorInitCallToBeRemoved(resourceResolver.InitMethod);

			resourceMethodsRestorer = new ResourceMethodsRestorer(module);
			if ((Operations.RenamerFlags & (RenamerFlags.RenameTypes | RenamerFlags.RenameNamespaces)) != 0)
				resourceMethodsRestorer.Find(DeobfuscatedFile, this);

			DumpEmbeddedAssemblies();
		}

		void DumpEmbeddedAssemblies() {
			foreach (var info in assemblyResolver.AssemblyInfos) {
				DeobfuscatedFile.CreateAssemblyFile(info.Data, info.SimpleName, info.Extension);
				AddResourceToBeRemoved(info.Resource, $"Embedded assembly: {info.AssemblyFullName}");
			}
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			resourceMethodsRestorer.Deobfuscate(blocks);
			assemblyResolver.Deobfuscate(blocks);
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			if (CanRemoveStringDecrypterType) {
				AddTypesToBeRemoved(stringDecrypter.Types, "String decrypter type");
				//AddTypeToBeRemoved(decrypterType.Type, "Decrypter type");
				AddTypesToBeRemoved(stringDecrypter.DynocodeTypes, "Dynocode type");
				AddResourceToBeRemoved(stringDecrypter.Resource, "Encrypted strings");
			}
			stringDecrypter.CloseServer();

			AddTypeToBeRemoved(assemblyResolver.Type, "Assembly resolver type");
			AddTypeToBeRemoved(assemblyResolver.OtherType, "Assembly resolver other type");
			AddTypeToBeRemoved(resourceResolver.Type, "Resource resolver type");
			AddTypeToBeRemoved(resourceMethodsRestorer.Type, "GetManifestResourceStream type");
			AddResourceToBeRemoved(resourceMethodsRestorer.Resource, "GetManifestResourceStream type resource");

			FixInterfaces();
			StringDecrypterBugWorkaround();
			base.DeobfuscateEnd();
		}

		void StringDecrypterBugWorkaround() {
			// There's a bug in Eazfuscator.NET when the VM and string encryption features are
			// enabled. The string decrypter's initialization code checks to make sure it's not
			// called by eg. a dynamic method. When it's called from the VM code, it is
			// called by MethodBase.Invoke() and the string decrypter antis set in causing it
			// to fail.
			// One way to work around this is to make sure the string decrypter has been called
			// once. That way, any VM code calling it won't trigger a failure.
			// We can put this code in <Module>::.cctor() since it gets executed before any
			// other code.
			// Note that we can't call the string decrypter from <Module>::.cctor() since
			// its DeclaringType property will return null (since it's the global type). We
			// must call another created class which calls the string decrypter.

			// You must use --dont-rename --keep-types --preserve-tokens and decrypt strings
			if (!Operations.KeepObfuscatorTypes || Operations.DecryptStrings == OpDecryptString.None ||
				(Operations.RenamerFlags & (RenamerFlags.RenameNamespaces | RenamerFlags.RenameTypes)) != 0)
				return;

			if (stringDecrypter.ValidStringDecrypterValue == null)
				return;

			var newType = module.UpdateRowId(new TypeDefUser(Guid.NewGuid().ToString("B"), module.CorLibTypes.Object.TypeDefOrRef));
			module.Types.Add(newType);
			var newMethod = module.UpdateRowId(new MethodDefUser("x", MethodSig.CreateStatic(module.CorLibTypes.Void), 0, MethodAttributes.Static | MethodAttributes.HideBySig));
			newType.Methods.Add(newMethod);
			newMethod.Body = new CilBody();
			newMethod.Body.MaxStack = 1;
			newMethod.Body.Instructions.Add(Instruction.CreateLdcI4(stringDecrypter.ValidStringDecrypterValue.Value));
			newMethod.Body.Instructions.Add(OpCodes.Call.ToInstruction(stringDecrypter.Method));
			newMethod.Body.Instructions.Add(OpCodes.Pop.ToInstruction());
			newMethod.Body.Instructions.Add(OpCodes.Ret.ToInstruction());

			var cctor = module.GlobalType.FindOrCreateStaticConstructor();
			var blocks = new Blocks(cctor);
			var block = blocks.MethodBlocks.GetAllBlocks()[0];
			block.Insert(0, OpCodes.Call.ToInstruction(newMethod));

			blocks.GetCode(out var allInstructions, out var allExceptionHandlers);
			DotNetUtils.RestoreBody(cctor, allInstructions, allExceptionHandlers);
		}

		protected override void Dispose(bool disposing) {
			if (disposing) {
				if (stringDecrypter != null)
					stringDecrypter.Dispose();
			}
			base.Dispose(disposing);
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.Method != null)
				list.Add(stringDecrypter.Method.MDToken.ToInt32());
			return list;
		}
	}
}
