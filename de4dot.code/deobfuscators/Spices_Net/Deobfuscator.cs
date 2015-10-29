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

using System.Collections.Generic;
using dnlib.DotNet;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Spices_Net {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Spices.Net";
		public const string THE_TYPE = "sn";
		const string DEFAULT_REGEX = @"!^[a-zA-Z0-9]{1,2}$&" + DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		BoolOption inlineMethods;
		BoolOption removeInlinedMethods;
		BoolOption removeNamespaces;
		BoolOption restoreResourceNames;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			inlineMethods = new BoolOption(null, MakeArgName("inline"), "Inline short methods", true);
			removeInlinedMethods = new BoolOption(null, MakeArgName("remove-inlined"), "Remove inlined methods", true);
			removeNamespaces = new BoolOption(null, MakeArgName("ns1"), "Clear namespace if there's only one class in it", true);
			restoreResourceNames = new BoolOption(null, MakeArgName("rsrc"), "Restore resource names", true);
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
				InlineMethods = inlineMethods.Get(),
				RemoveInlinedMethods = removeInlinedMethods.Get(),
				RemoveNamespaces = removeNamespaces.Get(),
				RestoreResourceNames = restoreResourceNames.Get(),
			});
		}

		protected override IEnumerable<Option> GetOptionsInternal() {
			return new List<Option>() {
				inlineMethods,
				removeInlinedMethods,
				removeNamespaces,
				restoreResourceNames,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		bool foundSpicesAttribute = false;
		bool startedDeobfuscating = false;

		StringDecrypter stringDecrypter;
		SpicesMethodCallInliner methodCallInliner;
		ResourceNamesRestorer resourceNamesRestorer;

		internal class Options : OptionsBase {
			public bool InlineMethods { get; set; }
			public bool RemoveInlinedMethods { get; set; }
			public bool RemoveNamespaces { get; set; }
			public bool RestoreResourceNames { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		protected override bool CanInlineMethods {
			get { return startedDeobfuscating ? options.InlineMethods : true; }
		}

		public override IEnumerable<IBlocksDeobfuscator> BlocksDeobfuscators {
			get {
				var list = new List<IBlocksDeobfuscator>();
				if (CanInlineMethods)
					list.Add(methodCallInliner);
				return list;
			}
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;

			if (options.RemoveNamespaces)
				this.RenamingOptions |= RenamingOptions.RemoveNamespaceIfOneType;
			else
				this.RenamingOptions &= ~RenamingOptions.RemoveNamespaceIfOneType;
		}

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(stringDecrypter.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);
			if (foundSpicesAttribute)
				val += 10;

			return val;
		}

		protected override void ScanForObfuscator() {
			methodCallInliner = new SpicesMethodCallInliner(module);
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.Find();
			FindSpicesAttributes();
		}

		void FindSpicesAttributes() {
			foreach (var type in module.Types) {
				switch (type.FullName) {
				case "NineRays.Decompiler.NotDecompile":
				case "NineRays.Obfuscator.Evaluation":
				case "NineRays.Obfuscator.SoftwareWatermarkAttribute":
					AddAttributeToBeRemoved(type, "Obfuscator attribute");
					foundSpicesAttribute = true;
					break;
				}
			}
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			methodCallInliner.Initialize(DeobfuscatedFile);

			if (options.RestoreResourceNames) {
				resourceNamesRestorer = new ResourceNamesRestorer(module);
				resourceNamesRestorer.Find();
			}

			stringDecrypter.Initialize();
			foreach (var info in stringDecrypter.DecrypterInfos) {
				staticStringInliner.Add(info.method, (method2, gim, args) => {
					return stringDecrypter.Decrypt(method2);
				});
			}
			DeobfuscatedFile.StringDecryptersAdded();

			startedDeobfuscating = true;
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			methodCallInliner.Deobfuscate(blocks);
			if (options.RestoreResourceNames)
				resourceNamesRestorer.Deobfuscate(blocks);
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			RemoveInlinedMethods();

			if (options.RestoreResourceNames) {
				resourceNamesRestorer.RenameResources();
				AddTypeToBeRemoved(resourceNamesRestorer.ResourceManagerType, "Obfuscator ResourceManager type");
				AddTypeToBeRemoved(resourceNamesRestorer.ComponentResourceManagerType, "Obfuscator ComponentResourceManager type");
			}

			if (Operations.DecryptStrings != OpDecryptString.None) {
				AddTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
				AddTypeToBeRemoved(stringDecrypter.EncryptedStringsType, "Encrypted strings field type");
				stringDecrypter.CleanUp();
			}

			base.DeobfuscateEnd();
		}

		void RemoveInlinedMethods() {
			if (!options.InlineMethods || !options.RemoveInlinedMethods)
				return;

			var unusedMethods = new UnusedMethodsFinder(module, methodCallInliner.GetInlinedMethods(), GetRemovedMethods()).Find();
			var removedTypes = methodCallInliner.GetInlinedTypes(unusedMethods);

			AddTypesToBeRemoved(removedTypes.GetKeys(), "Obfuscator methods type");
			foreach (var method in unusedMethods) {
				if (!removedTypes.Find(method.DeclaringType))
					AddMethodToBeRemoved(method, "Inlined method");
			}
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var info in stringDecrypter.DecrypterInfos)
				list.Add(info.method.MDToken.ToInt32());
			return list;
		}
	}
}
