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

namespace de4dot.code.deobfuscators.ILProtector {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "ILProtector";
		public const string THE_TYPE = "il";
		const string DEFAULT_REGEX = DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
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
			});
		}

		protected override IEnumerable<Option> GetOptionsInternal() {
			return new List<Option>() {
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		//Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;

		MainType mainType;
		StaticMethodsDecrypter staticMethodsDecrypter;
		DynamicMethodsRestorer dynamicMethodsRestorer;

		internal class Options : OptionsBase {
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
			//this.options = options;
		}

		protected override int DetectInternal() {
			return mainType.Detected ? 150 : 0;
		}

		protected override void ScanForObfuscator() {
			mainType = new MainType(module);
			mainType.Find();

			staticMethodsDecrypter = new StaticMethodsDecrypter(module, mainType);
			if (mainType.Detected)
				staticMethodsDecrypter.Find();

			if (mainType.Detected && !staticMethodsDecrypter.Detected)
				dynamicMethodsRestorer = new DynamicMethodsRestorer(module, mainType);

			if (mainType.Detected) {
				if (staticMethodsDecrypter.Detected)
					UpdateObfuscatorNameWith(staticMethodsDecrypter.Version);
				else
					UpdateObfuscatorNameWith(mainType.GetRuntimeVersionString());
			}
		}

		void UpdateObfuscatorNameWith(string version) {
			if (!string.IsNullOrEmpty(version))
				obfuscatorName += " " + version;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			if (mainType.Detected) {
				if (staticMethodsDecrypter.Detected) {
					staticMethodsDecrypter.Decrypt();
					RemoveObfuscatorJunk(staticMethodsDecrypter);
				}
				else if (dynamicMethodsRestorer != null) {
					Logger.v("Runtime file versions:");
					Logger.Instance.Indent();
					bool emailMe = false;
					foreach (var info in mainType.RuntimeFileInfos) {
						var version = info.GetVersion();
						emailMe |= version != null && version == new Version(1, 0, 7, 0);
						Logger.v("Version: {0} ({1})", version == null ? "UNKNOWN" : version.ToString(), info.PathName);
					}
					Logger.Instance.DeIndent();
					if (emailMe)
						Logger.n("**** Email me this program! de4dot@gmail.com");

					dynamicMethodsRestorer.Decrypt();
					RemoveObfuscatorJunk(dynamicMethodsRestorer);
				}
				else
					Logger.w("New ILProtector version. Can't decrypt methods (yet)");
			}
		}

		void RemoveObfuscatorJunk(MethodsDecrypterBase methodsDecrypter) {
			AddTypesToBeRemoved(methodsDecrypter.DelegateTypes, "Obfuscator method delegate type");
			AddResourceToBeRemoved(methodsDecrypter.Resource, "Encrypted methods resource");
			AddTypeToBeRemoved(mainType.InvokerDelegate, "Invoker delegate type");
			AddFieldToBeRemoved(mainType.InvokerInstanceField, "Invoker delegate instance field");
			foreach (var info in mainType.RuntimeFileInfos)
				AddMethodToBeRemoved(info.ProtectMethod, "Obfuscator 'Protect' init method");
			mainType.CleanUp();
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			return new List<int>();
		}
	}
}
