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

namespace de4dot.code.deobfuscators.Phoenix_Protector
{
    using de4dot.code.deobfuscators;
    using dnlib.DotNet;
    using System;
    using System.Collections.Generic;

    internal class Deobfuscator : DeobfuscatorBase
    {
        private bool foundPhoneixAttribute;
        private string obfuscatorName;
        private Options options;
        private StringDecrypter stringDecrypter;

        public Deobfuscator(Options options) : base(options)
        {
            this.obfuscatorName = "Phoneix Protector";
            this.foundPhoneixAttribute = false;
            this.options = options;
        }

        public override void DeobfuscateBegin()
        {
            base.DeobfuscateBegin();
            foreach (StringDecrypter.StringDecrypterInfo info in this.stringDecrypter.StringDecrypterInfos)
            {
                base.staticStringInliner.Add(info.method, (method, gim, args) => this.stringDecrypter.Decrypt((string) args[0]));
            }
            base.DeobfuscatedFile.StringDecryptersAdded();
        }

        public override void DeobfuscateEnd()
        {
            if (base.CanRemoveStringDecrypterType)
            {
                base.AddMethodsToBeRemoved(this.stringDecrypter.StringDecrypters, "String Decrypter Method");
                base.AddTypeToBeRemoved(this.stringDecrypter.Type, "String Derypter Type");
            }
            base.DeobfuscateEnd();
        }

        protected override int DetectInternal()
        {
            int num = 0;
            if (this.stringDecrypter.Detected)
            {
                num += 100;
            }
            if (this.foundPhoneixAttribute)
            {
                num += 10;
            }
            foreach (TypeDef def in base.module.Types)
            {
                if (def.FullName.Contains("OrangeHeapAttribute"))
                {
                    return 0;
                }
            }
            return num;
        }

        private void FindPhoneixAttribute()
        {
            foreach (TypeDef def in base.module.Types)
            {
                if (def.Namespace.StartsWith("?") && def.Namespace.EndsWith("?"))
                {
                    this.foundPhoneixAttribute = true;
                    break;
                }
            }
        }

        public override IEnumerable<int> GetStringDecrypterMethods()
        {
            List<int> list = new List<int>();
            foreach (MethodDef def in this.stringDecrypter.StringDecrypters)
            {
                list.Add(def.MDToken.ToInt32());
            }
            return list;
        }

        protected override void ScanForObfuscator()
        {
            this.stringDecrypter = new StringDecrypter(base.module);
            this.stringDecrypter.Find(base.DeobfuscatedFile);
            this.FindPhoneixAttribute();
        }

        public override string Name
        {
            get
            {
                return this.obfuscatorName;
            }
        }

        public override string Type
        {
            get
            {
                return "pp";
            }
        }

        public override string TypeLong
        {
            get
            {
                return "Phoneix Protector";
            }
        }

        internal class Options : DeobfuscatorBase.OptionsBase
        {
        }
    }
}

