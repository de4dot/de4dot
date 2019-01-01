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
using System.Collections;
using System.Collections.Generic;

#pragma warning disable CS0693, CS0414, CS0169, CS0108, CS0067

namespace Test.Rename.Dll {
	static class g {
		static public T m<T>(T t) {
			return t;
		}

		public delegate void Func1();
	}

	namespace test.fields.instance {
		class Class1 {
			public int var1 = g.m(1);
			public int var2 = g.m(2);
			protected int var3 = g.m(3);
			protected int var4 = g.m(4);
			private int var5 = g.m(5);
			private int var6 = g.m(6);
		}
		class Class2 : Class1 {
			public new int var1 = g.m(10);
			public new int var2 = g.m(20);
			protected new int var3 = g.m(30);
			protected new int var4 = g.m(40);
			private int var5 = g.m(50);
			private int var6 = g.m(60);
		}
		class Class3 : Class1 {
			public new int var1 = g.m(100);
			public new int var2 = g.m(200);
			protected new int var3 = g.m(300);
			protected new int var4 = g.m(400);
			private int var5 = g.m(500);
			private int var6 = g.m(600);
		}
	}

	namespace test.fields.Static {
		class Class1 {
			public static int var1 = g.m(1);
			public static int var2 = g.m(2);
			protected static int var3 = g.m(3);
			protected static int var4 = g.m(4);
			private static int var5 = g.m(5);
			private static int var6 = g.m(6);
		}
		class Class2 : Class1 {
			public new static int var1 = g.m(10);
			public new static int var2 = g.m(20);
			protected new static int var3 = g.m(30);
			protected new static int var4 = g.m(40);
			private static int var5 = g.m(50);
			private static int var6 = g.m(60);
		}
		class Class3 : Class1 {
			public new static int var1 = g.m(100);
			public new static int var2 = g.m(200);
			protected new static int var3 = g.m(300);
			protected new static int var4 = g.m(400);
			private static int var5 = g.m(500);
			private static int var6 = g.m(600);
		}
	}

	namespace test.props.instance {
		class Class1 {
			public int prop1 { get; set; }
			public int prop2 { get; set; }
			protected int prop3 { get; set; }
			protected int prop4 { get; set; }
			private int prop5 { get; set; }
			private int prop6 { get; set; }
		}
		class Class2 : Class1 {
			public new int prop1 { get; set; }
			public new int prop2 { get; set; }
			protected new int prop3 { get; set; }
			protected new int prop4 { get; set; }
			private int prop5 { get; set; }
			private int prop6 { get; set; }
		}
		class Class3 : Class1 {
			public new int prop1 { get; set; }
			public new int prop2 { get; set; }
			protected new int prop3 { get; set; }
			protected new int prop4 { get; set; }
			private int prop5 { get; set; }
			private int prop6 { get; set; }
		}
	}

	namespace test.props.Virtual {
		class Class1 {
			public virtual int prop1 { get; set; }
			public virtual int prop2 { get; set; }
			protected virtual int prop3 { get; set; }
			protected virtual int prop4 { get; set; }
		}
		class Class2 : Class1 {
			public override int prop1 { get; set; }
			public override int prop2 { get; set; }
			protected override int prop3 { get; set; }
			protected override int prop4 { get; set; }
		}
		class Class3 : Class1 {
			public override int prop1 { get; set; }
			public override int prop2 { get; set; }
			protected override int prop3 { get; set; }
			protected override int prop4 { get; set; }
		}
	}

	namespace test.props.Virtual.newslot {
		class Class1 {
			public virtual int prop1 { get; set; }
			public virtual int prop2 { get; set; }
			protected virtual int prop3 { get; set; }
			protected virtual int prop4 { get; set; }
		}
		class Class2 : Class1 {
			public new virtual int prop1 { get; set; }
			public new virtual int prop2 { get; set; }
			protected new virtual int prop3 { get; set; }
			protected new virtual int prop4 { get; set; }
		}
		class Class3 : Class1 {
			public new virtual int prop1 { get; set; }
			public new virtual int prop2 { get; set; }
			protected new virtual int prop3 { get; set; }
			protected new virtual int prop4 { get; set; }
		}
	}

	namespace test.props.Abstract {
		abstract class Class1 {
			public abstract int prop1 { get; set; }
			public abstract int prop2 { get; set; }
			protected abstract int prop3 { get; set; }
			protected abstract int prop4 { get; set; }
		}
		class Class2 : Class1 {
			public override int prop1 { get; set; }
			public override int prop2 { get; set; }
			protected override int prop3 { get; set; }
			protected override int prop4 { get; set; }
		}
		class Class3 : Class1 {
			public override int prop1 { get; set; }
			public override int prop2 { get; set; }
			protected override int prop3 { get; set; }
			protected override int prop4 { get; set; }
		}
	}

	namespace test.props.Interface1 {
		interface IFace1 {
			int prop1 { get; set; }
			int prop2 { get; set; }
		}
		class Class1 : IFace1 {
			public int prop1 { get; set; }
			public int prop2 { get; set; }
		}
	}

	namespace test.props.Interface2 {
		interface IFace1 {
			int prop1 { get; set; }
			int prop2 { get; set; }
		}
		interface IFace2 {
			int prop1 { get; set; }
			int prop2 { get; set; }
		}
		class Class1 : IFace1, IFace2 {
			public int prop1 { get; set; }
			public int prop2 { get; set; }
		}
	}

	namespace test.props.Interface3 {
		interface IFace1 {
			int prop1 { get; set; }
			int prop2 { get; set; }
		}
		interface IFace2 {
			int prop1 { get; set; }
			int prop2 { get; set; }
		}
		class Class1 : IFace1, IFace2 {
			public int prop1 { get; set; }
			public int prop2 { get; set; }
			int IFace1.prop1 { get; set; }
			int IFace1.prop2 { get; set; }
			int IFace2.prop1 { get; set; }
			int IFace2.prop2 { get; set; }
		}
	}

	namespace test.props.Interface4 {
		interface IFace1 {
			int prop1 { get; set; }
			int prop2 { get; set; }
		}
		interface IFace2 : IFace1 {
			new int prop1 { get; set; }
			new int prop2 { get; set; }
		}
		class Class1 : IFace2 {
			public int prop1 { get; set; }
			public int prop2 { get; set; }
		}
	}

	namespace test.props.Interface5 {
		interface IFace1 {
			int prop1 { get; set; }
			int prop2 { get; set; }
			int prop3 { get; set; }
			int prop4 { get; set; }
		}
		interface IFace2 {
			int prop5 { get; set; }
			int prop6 { get; set; }
		}
		class Class1 : IFace1, IFace2 {
			public int prop1 { get; set; }
			public int prop2 { get; set; }
			public int prop3 { get; set; }
			public int prop4 { get; set; }
			public int prop5 { get; set; }
			public int prop6 { get; set; }
		}
	}

	namespace test.props.Interface6 {
		interface IFace1 {
			int prop1 { get; set; }
		}
		interface IFace2 {
			int prop2 { get; set; }
		}
		interface IFace3 {
			int prop3 { get; set; }
		}
		interface IFace4 {
			int prop4 { get; set; }
		}
		interface IFace5 {
			int prop5 { get; set; }
		}
		class Class4 : IFace1, IFace2, IFace3 {
			public int prop1 { get; set; }
			public int prop2 { get; set; }
			public int prop3 { get; set; }
		}
		class Class5 : IFace1 {
			public int prop1 { get; set; }
		}
		class Class6 : IFace3, IFace4, IFace5 {
			public int prop3 { get; set; }
			public int prop4 { get; set; }
			public int prop5 { get; set; }
		}
	}

	namespace test.props.Interface7 {
		interface IFace1 {
			int prop1 { get; set; }
		}
		interface IFace2 {
			int prop2 { get; set; }
		}
		interface IFace3 {
			int prop3 { get; set; }
		}
		class Class1 {
		}
		class Class2 : Class1, IFace1 {
			public int prop1 { get; set; }
		}
		class Class3 : Class1, IFace2 {
			public int prop2 { get; set; }
		}
		class Class4 : Class3 {
		}
		class Class5 : Class3, IFace3 {
			public int prop3 { get; set; }
		}
	}

	namespace test.props.valuearg {
		class Class1 {
			int i;
			int Prop1 {
				get { return 1; }
				set { i = value; }
			}
		}
	}

	namespace test.events.instance {
		class Class1 {
			public event g.Func1 event1;
			public event g.Func1 event2;
			protected event g.Func1 event3;
			protected event g.Func1 event4;
			private event g.Func1 event5;
			private event g.Func1 event6;
		}
		class Class2 : Class1 {
			public new event g.Func1 event1;
			public new event g.Func1 event2;
			protected new event g.Func1 event3;
			protected new event g.Func1 event4;
			private event g.Func1 event5;
			private event g.Func1 event6;
		}
		class Class3 : Class1 {
			public new event g.Func1 event1;
			public new event g.Func1 event2;
			protected new event g.Func1 event3;
			protected new event g.Func1 event4;
			private event g.Func1 event5;
			private event g.Func1 event6;
		}
	}

	namespace test.events.Virtual {
		class Class1 {
			public virtual event g.Func1 event1;
			public virtual event g.Func1 event2;
			protected virtual event g.Func1 event3;
			protected virtual event g.Func1 event4;
		}
		class Class2 : Class1 {
			public override event g.Func1 event1;
			public override event g.Func1 event2;
			protected override event g.Func1 event3;
			protected override event g.Func1 event4;
		}
		class Class3 : Class1 {
			public override event g.Func1 event1;
			public override event g.Func1 event2;
			protected override event g.Func1 event3;
			protected override event g.Func1 event4;
		}
	}

	namespace test.events.Virtual.newslot {
		class Class1 {
			public virtual event g.Func1 event1;
			public virtual event g.Func1 event2;
			protected virtual event g.Func1 event3;
			protected virtual event g.Func1 event4;
		}
		class Class2 : Class1 {
			public new virtual event g.Func1 event1;
			public new virtual event g.Func1 event2;
			protected new virtual event g.Func1 event3;
			protected new virtual event g.Func1 event4;
		}
		class Class3 : Class1 {
			public new virtual event g.Func1 event1;
			public new virtual event g.Func1 event2;
			protected new virtual event g.Func1 event3;
			protected new virtual event g.Func1 event4;
		}
	}

	namespace test.events.Abstract {
		abstract class Class1 {
			public abstract event g.Func1 event1;
			public abstract event g.Func1 event2;
			protected abstract event g.Func1 event3;
			protected abstract event g.Func1 event4;
		}
		class Class2 : Class1 {
			public override event g.Func1 event1;
			public override event g.Func1 event2;
			protected override event g.Func1 event3;
			protected override event g.Func1 event4;
		}
		class Class3 : Class1 {
			public override event g.Func1 event1;
			public override event g.Func1 event2;
			protected override event g.Func1 event3;
			protected override event g.Func1 event4;
		}
	}

	namespace test.events.Interface1 {
		interface IFace1 {
			event g.Func1 event1;
			event g.Func1 event2;
		}
		class Class1 : IFace1 {
			public event g.Func1 event1;
			public event g.Func1 event2;
		}
	}

	namespace test.events.Interface2 {
		interface IFace1 {
			event g.Func1 event1;
			event g.Func1 event2;
		}
		interface IFace2 {
			event g.Func1 event1;
			event g.Func1 event2;
		}
		class Class1 : IFace1, IFace2 {
			public event g.Func1 event1;
			public event g.Func1 event2;
		}
	}

	namespace test.events.Interface3 {
		interface IFace1 {
			event g.Func1 event1;
			event g.Func1 event2;
		}
		interface IFace2 {
			event g.Func1 event1;
			event g.Func1 event2;
		}
		class Class1 : IFace1, IFace2 {
			public event g.Func1 event1;
			public event g.Func1 event2;
			event g.Func1 IFace1.event1 {
				add { }
				remove { }
			}
			event g.Func1 IFace1.event2 {
				add { }
				remove { }
			}
			event g.Func1 IFace2.event1 {
				add { }
				remove { }
			}
			event g.Func1 IFace2.event2 {
				add { }
				remove { }
			}
		}
	}

	namespace test.events.Interface4 {
		interface IFace1 {
			event g.Func1 event1;
			event g.Func1 event2;
		}
		interface IFace2 : IFace1 {
			new event g.Func1 event1;
			new event g.Func1 event2;
		}
		class Class1 : IFace2 {
			public event g.Func1 event1;
			public event g.Func1 event2;
		}
	}

	namespace test.events.Interface5 {
		interface IFace1 {
			event g.Func1 event1;
			event g.Func1 event2;
			event g.Func1 event3;
			event g.Func1 event4;
		}
		interface IFace2 {
			event g.Func1 event5;
			event g.Func1 event6;
		}
		class Class1 : IFace1, IFace2 {
			public event g.Func1 event1;
			public event g.Func1 event2;
			public event g.Func1 event3;
			public event g.Func1 event4;
			public event g.Func1 event5;
			public event g.Func1 event6;
		}
	}

	namespace test.events.Interface6 {
		interface IFace1 {
			event g.Func1 event1;
		}
		interface IFace2 {
			event g.Func1 event2;
		}
		interface IFace3 {
			event g.Func1 event3;
		}
		interface IFace4 {
			event g.Func1 event4;
		}
		interface IFace5 {
			event g.Func1 event5;
		}
		class Class4 : IFace1, IFace2, IFace3 {
			public event g.Func1 event1;
			public event g.Func1 event2;
			public event g.Func1 event3;
		}
		class Class5 : IFace1 {
			public event g.Func1 event1;
		}
		class Class6 : IFace3, IFace4, IFace5 {
			public event g.Func1 event3;
			public event g.Func1 event4;
			public event g.Func1 event5;
		}
	}

	namespace test.events.Interface7 {
		interface IFace1 {
			event g.Func1 event1;
		}
		interface IFace2 {
			event g.Func1 event2;
		}
		interface IFace3 {
			event g.Func1 event3;
		}
		class Class1 {
		}
		class Class2 : Class1, IFace1 {
			public event g.Func1 event1;
		}
		class Class3 : Class1, IFace2 {
			public event g.Func1 event2;
		}
		class Class4 : Class3 {
		}
		class Class5 : Class3, IFace3 {
			public event g.Func1 event3;
		}
	}

	namespace test.events.valuearg {
		class Class1 {
			g.Func1 f;
			event g.Func1 Event {
				add { f += value; }
				remove { f -= value; }
			}
		}
	}

	namespace test.methods.instance {
		class Class1 {
			public void meth1() { }
			public void meth2() { }
			protected void meth3() { }
			protected void meth4() { }
			private void meth5() { }
			private void meth6() { }
		}
		class Class2 : Class1 {
			public new void meth1() { }
			public new void meth2() { }
			protected new void meth3() { }
			protected new void meth4() { }
			private void meth5() { }
			private void meth6() { }
		}
		class Class3 : Class1 {
			public new void meth1() { }
			public new void meth2() { }
			protected new void meth3() { }
			protected new void meth4() { }
			private void meth5() { }
			private void meth6() { }
		}
	}

	namespace test.methods.Static {
		class Class1 {
			public static void meth1() { }
			public static void meth2() { }
			protected static void meth3() { }
			protected static void meth4() { }
			private static void meth5() { }
			private static void meth6() { }
		}
		class Class2 : Class1 {
			public new static void meth1() { }
			public new static void meth2() { }
			protected new static void meth3() { }
			protected new static void meth4() { }
			private static void meth5() { }
			private static void meth6() { }
		}
		class Class3 : Class1 {
			public new static void meth1() { }
			public new static void meth2() { }
			protected new static void meth3() { }
			protected new static void meth4() { }
			private static void meth5() { }
			private static void meth6() { }
		}
	}

	namespace test.methods.Virtual {
		class Class1 {
			public virtual void meth1() { }
			public virtual void meth2() { }
			protected virtual void meth3() { }
			protected virtual void meth4() { }
		}
		class Class2 : Class1 {
			public override void meth1() { }
			public override void meth2() { }
			protected override void meth3() { }
			protected override void meth4() { }
		}
		class Class3 : Class1 {
			public override void meth1() { }
			public override void meth2() { }
			protected override void meth3() { }
			protected override void meth4() { }
		}
	}

	namespace test.methods.Virtual.newslot {
		class Class1 {
			public virtual void meth1() { }
			public virtual void meth2() { }
			protected virtual void meth3() { }
			protected virtual void meth4() { }
		}
		class Class2 : Class1 {
			public new virtual void meth1() { }
			public new virtual void meth2() { }
			protected new virtual void meth3() { }
			protected new virtual void meth4() { }
		}
		class Class3 : Class1 {
			public new virtual void meth1() { }
			public new virtual void meth2() { }
			protected new virtual void meth3() { }
			protected new virtual void meth4() { }
		}
	}

	namespace test.methods.Abstract {
		abstract class Class1 {
			public abstract void meth1();
			public abstract void meth2();
			protected abstract void meth3();
			protected abstract void meth4();
		}
		class Class2 : Class1 {
			public override void meth1() { }
			public override void meth2() { }
			protected override void meth3() { }
			protected override void meth4() { }
		}
		class Class3 : Class1 {
			public override void meth1() { }
			public override void meth2() { }
			protected override void meth3() { }
			protected override void meth4() { }
		}
	}

	namespace test.methods.Interface1 {
		interface IFace1 {
			void meth1();
			void meth2();
		}
		class Class1 : IFace1 {
			public void meth1() { }
			public void meth2() { }
		}
	}

	namespace test.methods.Interface2 {
		interface IFace1 {
			void meth1();
			void meth2();
		}
		interface IFace2 {
			void meth1();
			void meth2();
		}
		class Class1 : IFace1, IFace2 {
			public void meth1() { }
			public void meth2() { }
		}
	}

	namespace test.methods.Interface3 {
		interface IFace1 {
			void meth1();
			void meth2();
		}
		interface IFace2 {
			void meth1();
			void meth2();
		}
		class Class1 : IFace1, IFace2 {
			public void meth1() { }
			public void meth2() { }
			void IFace1.meth1() { }
			void IFace1.meth2() { }
			void IFace2.meth1() { }
			void IFace2.meth2() { }
		}
	}

	namespace test.methods.Interface4 {
		interface IFace1 {
			void meth1();
			void meth2();
		}
		interface IFace2 : IFace1 {
			new void meth1();
			new void meth2();
		}
		class Class1 : IFace2 {
			public void meth1() { }
			public void meth2() { }
		}
	}

	namespace test.methods.Interface5 {
		interface IFace1 {
			void meth1();
			void meth2();
			void meth3();
			void meth4();
		}
		interface IFace2 {
			void meth5();
			void meth6();
		}
		class Class1 : IFace1, IFace2 {
			public void meth1() { }
			public void meth2() { }
			public void meth3() { }
			public void meth4() { }
			public void meth5() { }
			public void meth6() { }
		}
	}

	namespace test.methods.Interface6 {
		interface IFace1 {
			void meth1();
		}
		interface IFace2 {
			void meth2();
		}
		interface IFace3 {
			void meth3();
		}
		interface IFace4 {
			void meth4();
		}
		interface IFace5 {
			void meth5();
		}
		class Class4 : IFace1, IFace2, IFace3 {
			public void meth1() { }
			public void meth2() { }
			public void meth3() { }
		}
		class Class5 : IFace1 {
			public void meth1() { }
		}
		class Class6 : IFace3, IFace4, IFace5 {
			public void meth3() { }
			public void meth4() { }
			public void meth5() { }
		}
	}

	namespace test.methods.Interface7 {
		interface IFace1 {
			void meth1();
		}
		interface IFace2 {
			void meth2();
		}
		interface IFace3 {
			void meth3();
		}
		class Class1 {
		}
		class Class2 : Class1, IFace1 {
			public void meth1() { }
		}
		class Class3 : Class1, IFace2 {
			public void meth2() { }
		}
		class Class4 : Class3 {
		}
		class Class5 : Class3, IFace3 {
			public void meth3() { }
		}
	}

	namespace test.methods.signatures {
		enum consts {
			val1, val2, val3,
		}
		struct data1 {
			int i;
		}
		struct data2 {
			int i;
		}
		class Class1 {
			void meth1() { }
			void meth1(int i) { }
			unsafe void meth1(int* i) { }
			void meth1(int i, int j) { }
			void meth1(short i) { }
			void meth1(string s) { }
			void meth1(consts c) { }
			void meth1(data1 d) { }
			void meth1(data2 d) { }
			void meth1(List<int> l1, List<int> l2) { }
			void meth1(List<short> l1, List<short> l2) { }
			void meth1(List<int> l1, List<short> l2) { }
			void meth1(List<short> l1, List<int> l2) { }
			void meth1(List<List<List<List<int>>>> l1, List<int> l2) { }
			void meth1(List<List<List<List<int>>>> l1, List<List<int>> l2) { }
			void meth1(List<List<List<List<int>>>> l1, List<List<List<int>>> l2) { }
			void meth1(List<List<List<List<int>>>> l1, List<List<List<List<int>>>> l2) { }
			void meth1(List<List<List<List<int>>>> l1, List<List<List<List<short>>>> l2) { }
			void meth1(List<List<List<List<short>>>> l1, List<List<List<List<int>>>> l2) { }
			void meth1(List<List<List<List<short>>>> l1, List<List<List<List<short>>>> l2) { }
			void meth2() { }
			void meth2(int i) { }
			void meth2(int i, int j) { }
		}
	}

	namespace test.interfaces.test1 {
		interface IFace1 {
			void meth1();
			void meth1(int i);
			void meth1(string s);
			event g.Func1 Event1;
			int Prop1 { get; set; }
			int Prop2 { get; set; }
			string Prop3 { get; set; }
		}
		interface IFace2 : IFace1 {
			void meth1();
			void meth1(int i);
			void meth1(string s);
			event g.Func1 Event1;
			int Prop1 { get; set; }
			int Prop2 { get; set; }
			string Prop3 { get; set; }
		}
		class Class1 : IFace2 {
			public void meth1() { }
			public void meth1(int i) { }
			public void meth1(string s) { }
			public event g.Func1 Event1;
			public int Prop1 { get; set; }
			public int Prop2 { get; set; }
			public string Prop3 { get; set; }
		}
	}

	namespace test.enums.test1 {
		enum consts1 { }
		enum consts2 { a, b, c, d, e }
	}

	namespace test.structs.test1 {
		struct d1 { }
		struct d2 {
			int i;
			int j;
			string s;
			d1 d1;
		}
	}

	namespace test.structs.test2 {
		interface IFace1 {
			int prop1 { get; set; }
			string prop2 { get; set; }
			object meth1(int i);
		}
		interface IFace2 {
			int prop3 { get; set; }
			void meth1(int i);			// different return type
		}
		struct d1 : IFace1, IFace2 {
			public int prop1 { get; set; }
			public string prop2 { get; set; }
			public object meth1(int i) { return null; }
			public int prop3 { get; set; }
			void IFace2.meth1(int i) { }
		}
	}

	namespace test.variables.test1 {
		class Class1 {
			byte aByte = 123;
			byte[] anArray = new byte[11];
			byte[][] anArray2 = new byte[11][];
			List<byte[]> aList = new List<byte[]>();
			short aShort;
			int anInt;
			string aString;
			Class1 aClass;
		}
	}

	namespace test.generic.types.methods1 {
		class Class1<T, U, V> {
			T a;
			U b;
			V c;
			void meth1(T a) { }
			void meth1(U a) { }
			void meth1(V a) { }
			void meth1(List<T> a) { }
			void meth1(List<U> a) { }
			void meth1(List<V> a) { }
		}
	}

	namespace test.generic.types.methods2 {
		class Class1<T, U, V> {
			void meth1(int i) { }
			void meth1<W>(int i) { }
			void meth1<W, X>(int i) { }

			void meth1<W>(W w) { }
			void meth1<W>(T t) { }
			void meth1<W>(U u) { }
			void meth1<W>(V v) { }

			void meth1(T t, T t2) { }
			void meth1(U u, U u2) { }
			void meth1(V v, V v2) { }

			void meth1(T t, U u) { }
			void meth1(T t, V V) { }
			void meth1(U u, T t) { }
			void meth1(U u, V v) { }
			void meth1(V v, T t) { }
			void meth1(V v, U u) { }

			void meth1<W>(W w, W w2) { }
			void meth1<W>(T t, T t2) { }
			void meth1<W>(U u, U u2) { }
			void meth1<W>(V v, V v2) { }
			void meth1<W>(W w, T t) { }
			void meth1<W>(W w, U u) { }
			void meth1<W>(W w, V v) { }
			void meth1<W>(T t, W w) { }
			void meth1<W>(U u, W w) { }
			void meth1<W>(V v, W w) { }
		}
	}

	namespace test.generic.types.instance {
		class Class1<T, U> {
			public T meth1(T t) { return t; }
			public U meth1(U u) { return u; }
			public V meth1<V>(T t) { return default(V); }
			public V meth2<V>(V v) { return v; }
		}

		static class Class2 {
			static void meth1() {
				var t = new Class1<int, string>();
				t.meth1(123);
				t.meth1("hello");
				t.meth1<int>(45);
				t.meth1<object>(45);
				t.meth2<int>(45);
				t.meth2<object>(45);
				var u = new Class1<string, int>();
				u.meth1(123);
				u.meth1("hello");
				u.meth2<int>(45);
				u.meth2<object>(45);
			}
		}
	}

	namespace test.generic.types.Interface.test1 {
		interface IFace<T, V> {
			T Prop1 { get; set; }
			event g.Func1 Event;
			V meth1(V v);
			V meth1<W>(W w, W w2);
			V meth1<W>(V v, W w);
			V meth1<W>(W w, V v);
		}
		class Class1 : IFace<int, string> {
			public int Prop1 { get; set; }
			public event g.Func1 Event;
			public string meth1(string v) { return ""; }
			public string meth1<W>(W w, W w2) { return ""; }
			public string meth1<W>(string v, W w) { return ""; }
			public string meth1<W>(W w, string v) { return ""; }
		}
		class Class2 : IFace<string, int> {
			public string Prop1 { get; set; }
			public event g.Func1 Event;
			public int meth1(int v) { return 1; }
			public int meth1<W>(W w, W w2) { return 1; }
			public int meth1<W>(int v, W w) { return 1; }
			public int meth1<W>(W w, int v) { return 1; }
		}
		static class Class3 {
			static void meth1() {
				var c1 = new Class1();
				c1.Prop1 += 1;
				c1.meth1("");
				c1.meth1<int>(1, 2);
				c1.meth1<Exception>(new Exception(), new Exception());
				c1.meth1<Exception>("1", new Exception());
				c1.meth1<Exception>(new Exception(), "2");

				var c2 = new Class2();
				c2.Prop1 += 1;
				c2.meth1(1);
				c2.meth1<short>((short)1, (short)2);
				c2.meth1<Exception>(new Exception(), new Exception());
				c2.meth1<Exception>(1, new Exception());
				c2.meth1<Exception>(new Exception(), 2);
			}
		}
	}

	namespace test.generic.types.Interface.test2 {
		interface IFace<T> {
			void meth1();
		}
		class Class1 : IFace<int>, IFace<string> {
			void IFace<int>.meth1() { }
			void IFace<string>.meth1() { }
		}
	}

	namespace test.generic.types.Interface.test3 {
		interface IFace<T> {
			void meth1();
		}
		class Class1 : IFace<List<List<int>>>, IFace<List<List<string>>> {
			void IFace<List<List<int>>>.meth1() { }
			void IFace<List<List<string>>>.meth1() { }
		}
	}

	namespace test.generic.types.Interface.test4 {
		class Class1<T, U> {
			public interface IFace<V> {
				void meth1();
			}
		}
		class Class2<T, U, V> : Class1<T, U>.IFace<V> {
			void Class1<T, U>.IFace<V>.meth1() { }
		}
	}

	namespace test.generic.types.Interface.test5 {
		class Class1<T, U, V> {
			public virtual void meth1(T t) { }
			public virtual void meth1(U u) { }
			public virtual void meth1(V v) { }
		}
		class Class2<T> : Class1<int, string, T> {
			public override void meth1(int t) { }
			public override void meth1(string u) { }
			public override void meth1(T v) { }
		}
	}

	namespace test.generic.types.Interface.test6 {
		interface IFace<T, U> {
			void meth1(T t);
			void meth2(U u);
		}
		class Class1 : IFace<int, List<string>> {
			public void meth1(int t) { }
			public void meth2(List<string> u) { }
		}
	}

	namespace test.generic.types.Interface.test7 {
		interface IFace<T> {
			void meth1(T t);
			void meth2(T t);
			void meth3(T t);
		}
		abstract class Abstract1 : IFace<int> {
			public virtual void meth1(int t) { }
			public abstract void meth2(int t);
			public abstract void meth3(int t);
		}
		abstract class Abstract2 : Abstract1 {
			public override void meth2(int t) { }
		}
		class Class1 : Abstract2 {
			public override void meth3(int t) { }
		}
	}

	namespace test.generic.types.Interface.test8 {
		interface IFace<T> {
			void meth1(T t);
			void meth2(T t);
			void meth3(T t);
		}
		abstract class Abstract1<T> : IFace<T> {
			public virtual void meth1(T t) { }
			public abstract void meth2(T t);
			public abstract void meth3(T t);
		}
		abstract class Abstract2<T> : Abstract1<T> {
			public override void meth2(T t) { }
		}
		class Class1 : Abstract2<int> {
			public override void meth3(int t) { }
		}
	}

	namespace test.generic.types.Interface.test9 {
		interface IFace<T> {
			void meth1(T t);
		}
		class Class1<T, U> : IFace<T> {
			public virtual void meth1(T t) { }
		}
		class Class2<T, U> : Class1<T, U>, IFace<T> {
			public override void meth1(T t) { }
		}
		class Class3<T, U> : Class1<T, U>, IFace<U> {
			public override void meth1(T t) { }
			public void meth1(U u) { }
		}
	}

	namespace test.generic.types.Interface.test10 {
		interface IFace1<T> {
		}
		interface IFace2<U> {
			void meth1(IFace1<U> iface1);
		}
		class Class1<V> : IFace2<V> {
			public void meth1(IFace1<V> iface1) { }
		}
		class Class2<V> : IFace2<IFace1<V>> {
			public void meth1(IFace1<IFace1<V>> iface1) { }
		}
		class Class3 : IFace2<int>, IFace2<string> {
			public void meth1(IFace1<int> iface1) { }
			public void meth1(IFace1<string> iface1) { }
		}
		class Class4 : IFace2<IFace1<int>>, IFace2<IFace1<string>> {
			public void meth1(IFace1<IFace1<int>> iface1) { }
			public void meth1(IFace1<IFace1<string>> iface1) { }
		}
	}

	namespace test.generic.types.cls.name {
		class Class1<T, U> {
			class Class11 {
				class Class111 { }
				class Class112<V> { }
				interface IFace111 { }
				interface IFace112<V> { }
			}
			class Class12<V> {
				class Class121 { }
				class Class122<W> { }
				interface IFace121 { }
				interface IFace122<W> { }
			}
			class Class13<V, W, X, Y> {
				class Class131 { }
				class Class132<W> { }
				interface IFace131 { }
				interface IFace131<W> { }
			}
			interface IFace1 { }
			interface IFace2<V> { }
			interface IFace2<V, W, X> { }
		}
	}

	namespace test.generic.methods.test1 {
		class Class1 {
			public void meth1<T>(T t) { }
			public void meth2<T>(T t) { }
			public void meth1<T>(T t, int i) { }
			public void meth1<T>(int i, T t) { }
			public void meth1<T, U, V>(U u, V v, T t) { }
		}

		static class Class2 {
			static void meth1() {
				var v = new Class1();
				v.meth1<int>(123);
				v.meth1<object>(new Class1());
				v.meth2<int>(45);
				v.meth1<string>("", 123);
				v.meth1<string>(123, "");
				v.meth1<int, string, Exception>("", new Exception(), 123);
			}
		}
	}

	namespace test.generic.methods.test2 {
		class Class1 {
		}
		interface IFace1<T> {
			T meth1();
			void meth1(T t);
			void meth1(int i);
			void meth1(long i);
			void meth2(int i);
		}
		interface IFace2 {
			Class1 meth1();
			void meth1(Class1 t);
		}
		class Class2 : IFace1<Class1>, IFace2 {
			public Class1 meth1() { return null; }
			public void meth1(Class1 t) { }
			public void meth1(int i) { }
			public void meth1(long i) { }
			public void meth2(int i) { }
		}
	}

	namespace test.Override {
		class Class1 : IEqualityComparer<int>, IEqualityComparer<string> {
			bool IEqualityComparer<int>.Equals(int x, int y) {
				return true;
			}
			int IEqualityComparer<int>.GetHashCode(int obj) {
				return 1;
			}
			bool IEqualityComparer<string>.Equals(string x, string y) {
				return true;
			}
			int IEqualityComparer<string>.GetHashCode(string obj) {
				return 1;
			}
		}
	}

	namespace test.nested.types.test1 {
		class Class1 {
			public class Class2 {
				public virtual void meth1() { }
			}
			public abstract class Class3 {
				public abstract void meth1();
			}
		}
		class Class4 : Class1.Class2 {
			public override void meth1() { }
		}
		class Class5 : Class1.Class3 {
			public override void meth1() { }
		}
	}

	namespace test.nested.types.test2 {
		class Class1 {
			public interface IFace1 {
				void meth1();
			}
		}
		class Class2 : Class1.IFace1 {
			public void meth1() { }
		}
		class Class3 : Class1.IFace1 {
			void Class1.IFace1.meth1() { }
		}
	}

	namespace test.nested.types.test3 {
		class Class1 {
			public abstract class Class2<T, U> {
				public virtual void meth1(T t) { }
				public virtual void meth1(U u) { }
				public abstract void meth1(T t, U u);
			}
			public interface IFace1<T, U> {
				void meth1(T t);
				void meth1(U u);
			}
		}
		class Class3 : Class1.Class2<int, string> {
			public override void meth1(int t) { }
			public override void meth1(string u) { }
			public override void meth1(int t, string u) { }
		}
		class Class4 : Class1.IFace1<int, string> {
			public void meth1(int t) { }
			public void meth1(string u) { }
		}
		class Class5 : Class1.IFace1<int, string> {
			void Class1.IFace1<int, string>.meth1(int t) { }
			void Class1.IFace1<int, string>.meth1(string u) { }
		}
	}

	namespace test.generic.parameters {
		class Class1<T, U, V, W, X, Y, Z> {
			class Class2<A, B, C> {
			}
			interface IFace1<D, E, F> {
			}
			class Class3<T, U, V, A, B, C> {
			}
			void meth1<T, U, V, W, X, Y, Z>() {
			}
		}
		class CLass4 {
			void meth1<T, U, V, W, X, Y, Z>() {
			}
		}
	}

	namespace test.Class.names {
		public class Global1 {
			public class Global11 {
				public class Global111 { }
				public interface IGlobal111 { }
				protected internal class Global112 { }
				protected internal interface IGlobal112 { }
				private class Local111 { }
				private interface ILocal111 { }
				internal class Local112 { }
				internal interface ILocal112 { }
			}
			public interface IGlobal11 { }
			private class Local11 { }
			private interface ILocal11 { }
		}
		internal class Local2 {
			public class Local21 {
				public class Local211 { }
				public interface ILocal211 { }
				private class Local212 { }
				private interface ILocal212 { }
			}
			public interface ILocal21 { }
			private class Local22 {
				public class Local221 { }
				public interface ILocal221 { }
				private class Local222 { }
				private interface ILocal222 { }
			}
			internal class Local23 {
				public class Local231 { }
				public interface ILocal231 { }
				private class Local232 { }
				private interface ILocal232 { }
			}
			private interface ILocal22 { }
		}
	}

	namespace test.pub1 {
		public abstract class Class1 {
			protected int i = 123;
			public int Prop1 { get; set; }
			public abstract int Prop2 { get; }
			public abstract int Prop3 { set; }
			public virtual void meth1(int i) { }
			public abstract void meth1(string s);
		}
	}

	namespace test.pub2 {
		public interface IFace1 {
			void meth1(int i);
			void meth1(string s);
		}
	}

	namespace test.pub3 {
		public class Class1<T> {
			public interface IFace1<U> {
				void meth1(T t);
				void meth1(U u);
				void meth1<V>(V v);
			}
		}
	}

	namespace test.pub4 {
		public class Class1 {
			public class EnclosedClass {
				public virtual void meth1() { }
				public virtual void meth1(int i) { }
			}
		}
	}

	namespace test.pub5 {
		public class Class1 {
			public void meth1() { }
		}
		public interface IFace1 {
			void meth1();
		}
	}

	namespace test.inheriting.Interface.methods1 {
		class Class1 {
			public virtual bool meth1() { return true; }
			public virtual void meth2() { }
			public virtual void meth2(int i) { }
			public virtual int Prop1 { get; set; }
			public virtual void metht<T>(T t) { }
			public virtual event g.Func1 event1;
		}
		interface IFace2 {
			bool meth1();
			void meth2();
			void meth2(int i);
			void meth3(string s);
			int Prop1 { get; set; }
			void metht<T>(T t);
			event g.Func1 event1;
		}
		class Class2 : Class1, IFace2 {
			public void meth3(string s) { }
		}
		interface IFace3 {
			bool meth1();
			void meth2();
			void meth2(int i);
			void meth3(string s);
			int Prop1 { get; set; }
			void metht<T>(T t);
			event g.Func1 event1;
		}
		class Class3 : Class1, IFace3 {
			public void meth3(string s) { }
		}
	}

	namespace test.inheriting.Interface.methods2 {
		interface IFace1 {
			void meth1();
			void meth1(int i);
		}
		interface IFace2 {
			void meth1(int j);
		}
		class Class1 { }
		class Class2 : Class1, IFace1 {
			public virtual void meth1() { }
			public virtual void meth1(int k) { }
		}
		class Class3 : Class1, IFace2 {
			public virtual void meth1(int l) { }
		}
	}

	namespace test.inheriting.Interface.methods3 {
		class Class1 {
			// The C# compiler will automatically convert this to a virtual method!
			public void meth1() { }
		}
		interface IFace1 {
			void meth1();
		}
		class Class2 : Class1, IFace1 {
		}
	}

	namespace test.inheriting.Interface.methods4 {
		class Class1<T> {
			public virtual void meth1(T t) { }
			public virtual T Prop0 { get; set; }
			public virtual T Prop1 { get; set; }
			public virtual void methu<U>(T t) { }
			public virtual void methu<U>(U u) { }
		}
		interface IFace1<T> {
			void meth1(T t);
			T Prop1 { get; set; }
			void methu<U>(T t);
			void methu<U>(U u);
		}
		class Class2<T> : Class1<T>, IFace1<T> {

		}
	}

	namespace test.Virtual.methods1 {
		class Class1 {
			public virtual void meth1() { }
		}
		class Class2 : Class1 {
			public virtual void meth2() { }
		}
		class Class3 : Class2 {
			public override void meth1() { }
		}
		class Class4 : Class2 {
			public override void meth2() { }
		}
	}

	namespace test.Virtual.methods2 {
		abstract class Class1 {
			public abstract void meth1();
		}
		abstract class Class2 : Class1 {
			public virtual void meth2() { }
		}
		class Class3 : Class2 {
			public override void meth1() { }
		}
		class Class4 : Class2 {
			public override void meth1() { }
		}
		abstract class Class5 : Class2 {
			public override void meth2() { }
		}
		class Class6 : Class5 {
			public override void meth1() { }
		}
		class Class7 : Class5 {
			public override void meth1() { }
		}
	}

	namespace test.Virtual.methods3 {
		interface IFace1 {
			int meth1();
		}
		class Class1 : IFace1 {
			public int meth1() { return 0; }
		}
		class Class2 : Class1 { }
		interface IFace2 {
			bool meth2();
		}
		class Class3 : Class2, IFace2 {
			public virtual bool meth2() { return true; }
		}
		interface IFace3 {
			bool meth3();
			void meth1(bool b);
		}
		class Class4 : Class3, IFace3 {
			public bool meth3() { return true; }
			public void meth1(bool b) { }
		}
		class Class5 : Class4 {
			public override bool meth2() { return true; }
		}
	}

	namespace test.Virtual.properties.overrides {
		class Class1 : ICollection {
			int ICollection.Count {
				get { return 0; }
			}
			bool ICollection.IsSynchronized {
				get { return false; }
			}
			object ICollection.SyncRoot {
				get { return null; }
			}
			void ICollection.CopyTo(Array array, int index) {
			}
			IEnumerator IEnumerable.GetEnumerator() {
				return null;
			}
		}
	}

	namespace test.Virtual.properties.names {
		class Class1<T> {
			public virtual byte[] Prop1 { get; set; }
			public virtual unsafe byte* Prop2 {
				get { throw new NotImplementedException(); }
				set { }
			}
			public virtual T Prop3 { get; set; }
			public virtual T[] Prop4 { get; set; }
			public virtual int this[int i] {
				get { return 0; }
				set { }
			}
			public virtual int this[string s] {
				get { return 0; }
				set { }
			}
		}
	}
}
