using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Collections.Generic;

namespace PLC
{
    public class CLRGenerator : IGenerator
    {
        readonly Dictionary<string, FieldBuilder> _globalTable;
        readonly Dictionary<string, Dictionary<string, LocalBuilder>> _methodLocalsTable;
        readonly Dictionary<string, MethodBuilder> _methodTable;
        MethodBuilder? _methodBuilder;
        TypeBuilder? _typeBuilder;
        AssemblyBuilder? _asmb;
        ModuleBuilder? _modb;
        ILGenerator? _il;
        Procedure? _proc;

        public CLRGenerator(ParsedProgram program)
        {
            Program = program;
            foreach (Identity constant in program.Block.Constants) {
                program.Globals.Add(constant);
            }
            foreach (Identity variable in program.Block.Variables) {
                program.Globals.Add(variable);
            }
            _globalTable = new Dictionary<string, FieldBuilder>();
            _methodTable = new Dictionary<string, MethodBuilder>();
            _methodLocalsTable = new Dictionary<string, Dictionary<string, LocalBuilder>>();
        }
        
        public ParsedProgram Program { get; }

        public IEnumerable<string> Generate()
        {
            yield return "Text output is not available for this back-end -- call Compile() to generate a binary";
        }

        public int Compile(string filename)
        {
            string outputPath = filename;
            string moduleName = Path.GetFileNameWithoutExtension(filename);
            string extension = Path.GetExtension(filename);
            bool isDll = extension.Equals(".dll", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(extension))
            {
                outputPath = filename + ".exe";
                moduleName = filename;
            }
            
            AssemblyName asmName = new(moduleName);
            _asmb = new PersistedAssemblyBuilder(asmName, typeof(object).Assembly);
            _modb = _asmb.DefineDynamicModule(moduleName + (string.IsNullOrEmpty(extension) ? ".exe" : extension));
            _typeBuilder = _modb.DefineType("plc");
            // Generate constant declarations
            foreach (Identity constant in Program.Block.Constants) {
                var fieldBuilder = _typeBuilder.DefineField(constant.Name, typeof(int), FieldAttributes.Static | FieldAttributes.Private | FieldAttributes.Literal);
                fieldBuilder.SetConstant(Int32.Parse(constant.Value));
            }
            
            // Variable declarations
            foreach (Identity variable in Program.Block.Variables) {
                var fieldBuilder = _typeBuilder.DefineField(variable.Name, typeof(int), FieldAttributes.Static | FieldAttributes.Private);
                _globalTable.Add(variable.Name, fieldBuilder);
            }
            
            // Procedure definitions
            foreach (Procedure proc in Program.Block.Procedures)
            {
                _proc = proc;
                GenerateProcedure(_proc);
            }
            
            // Main program
            _proc = new Procedure() {Name = "Main", Block = new Block() {Statement = Program.Block.Statement}};
            GenerateProcedure(_proc, true);
            
            // Actually write out the assembly
            _typeBuilder!.CreateType();
            _modb!.CreateGlobalFunctions();

            // Get the entry point method for the PE header
            _methodTable.TryGetValue("Main", out var mainMethod);

            // Generate metadata and IL stream for the assembly
            MetadataBuilder metadataBuilder = (_asmb as PersistedAssemblyBuilder)!.GenerateMetadata(
                out BlobBuilder ilStream, out BlobBuilder mappedFieldData);

            // Build the PE file with entry point if this is an exe
            ManagedPEBuilder peBuilder = new(
                header: isDll ? PEHeaderBuilder.CreateLibraryHeader() : PEHeaderBuilder.CreateExecutableHeader(),
                metadataRootBuilder: new MetadataRootBuilder(metadataBuilder),
                ilStream: ilStream,
                mappedFieldData: mappedFieldData,
                entryPoint: mainMethod != null ? MetadataTokens.MethodDefinitionHandle(mainMethod.MetadataToken) : default);

            BlobBuilder peBlob = new();
            peBuilder.Serialize(peBlob);

            // Write the assembly to disk
            using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                peBlob.WriteContentTo(fileStream);
            }

            // Create runtimeconfig.json so the assembly can be run with 'dotnet'
            if (outputPath.EndsWith(".exe") || outputPath.EndsWith(".dll"))
            {
                string runtimeConfigPath = Path.ChangeExtension(outputPath, ".runtimeconfig.json");
                string runtimeConfig = "{\n" +
                    "  \"runtimeOptions\": {\n" +
                    "    \"tfm\": \"net10.0\",\n" +
                    "    \"frameworks\": [\n" +
                    "      {\n" +
                    "        \"name\": \"Microsoft.NETCore.App\",\n" +
                    "        \"version\": \"10.0.0\"\n" +
                    "      }\n" +
                    "    ]\n" +
                    "  }\n" +
                    "}";
                File.WriteAllText(runtimeConfigPath, runtimeConfig);
            }

            return 0; // Success!!
        }

        void GenerateProcedure(Procedure proc, bool procIsEntryPoint = false)
        {
            _proc = proc;
            MethodAttributes attrs = MethodAttributes.Static | MethodAttributes.Public;
            if (procIsEntryPoint)
            {
                attrs |= MethodAttributes.HideBySig;
            }
            _methodBuilder = _typeBuilder!.DefineMethod(proc.Name, attrs, typeof(void), Type.EmptyTypes);
            if (procIsEntryPoint)
            {
                // For .NET 10, the entry point is recognized by the "Main" method name
                // and the runtimeconfig.json file
            }
            _methodTable[proc.Name] = _methodBuilder;
            _il = _methodBuilder.GetILGenerator();
            GenerateBlock(proc.Block);
            _il!.Emit(OpCodes.Ret);
        }
        void GenerateBlock(Block block)
        {
            Dictionary<string, LocalBuilder> localTable = new();
            _methodLocalsTable[_proc!.Name] = localTable;
            foreach (Identity variable in block.Variables)
            {
                localTable[variable.Name] = _il!.DeclareLocal(typeof(int));
                _proc.Locals.Add(variable);
            }
            GenerateStatement(block.Statement);
        }

        void GenerateStatement(Statement statement)
        {
            if (statement is WriteStatement)
            {
                var writeStatement = (WriteStatement) statement;
                if (String.IsNullOrEmpty(writeStatement.Message))
                {
                    GenerateExpression(writeStatement.Expression);
                    _il!.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", new[] { typeof(int) }));
                }
                else
                {
                    _il!.Emit(OpCodes.Ldstr, writeStatement.Message);
                    _il!.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", new[] { typeof(string) }));
                }
            }
            else if (statement is ReadStatement)
            {
                var readStatement = (ReadStatement) statement;
                if (!String.IsNullOrEmpty(readStatement.Message))
                {
                    _il!.Emit(OpCodes.Ldstr, readStatement.Message);
                    _il!.Emit(OpCodes.Call, typeof(Console).GetMethod("Write", new[] { typeof(string) }));
                }
                _il!.Emit(OpCodes.Call, 
                    typeof(Console).GetMethod("ReadLine", BindingFlags.Public | BindingFlags.Static, null,  new Type[] { }, null));

                var localsTable = _methodLocalsTable[_proc!.Name];
                if (localsTable.ContainsKey(readStatement.IdentityName))
                {
                    _il!.Emit(OpCodes.Ldloca, localsTable[readStatement.IdentityName]);
                }
                else
                {
                    _il!.Emit(OpCodes.Ldsflda, _globalTable[readStatement.IdentityName]);
                }
                // This typeof(int).MakeRefType() thing took me at least an hour to figure out :-)
                _il!.Emit(OpCodes.Call, typeof(int).GetMethod("TryParse", new[] { typeof(string), typeof(int).MakeByRefType()}));
                _il!.Emit(OpCodes.Pop);
            }
            else if (statement is AssignmentStatement)
            {
                var assignmentStatement = (AssignmentStatement) statement;
                GenerateExpression(assignmentStatement.Expression);
                StoreIdentityName(assignmentStatement.IdentityName);
            }
            else if (statement is CallStatement)
            {
                var cs = (CallStatement) statement;
                _il!.Emit(OpCodes.Call, _methodTable[cs.ProcedureName]);
            }
            else if (statement is CompoundStatement)
            {
                var bs = (CompoundStatement) statement;
                foreach (Statement st in bs.Statements.Where(x => !x.SkipGeneration))
                {
                    GenerateStatement(st);
                }
            }
            else if (statement is IfStatement)
            {
                var iff = (IfStatement) statement;
                var label = _il!.DefineLabel();
                BranchWhenFalse(iff.Condition, label);
                GenerateStatement(iff.Statement);
                _il!.MarkLabel(label);
            }
            else if (statement is DoWhileStatement)
            {
                var dw = (DoWhileStatement) statement;
                var label = _il!.DefineLabel();
                _il!.MarkLabel(label);
                GenerateStatement(dw.Statement);
                BranchWhenTrue(dw.Condition, label);
            }
            else if (statement is WhileStatement)
            {
                var startLabel = _il!.DefineLabel();
                var endLabel = _il!.DefineLabel();
                _il!.MarkLabel(startLabel);
                var ws = (WhileStatement) statement;
                BranchWhenFalse(ws.Condition, endLabel);
                GenerateStatement(ws.Statement);
                _il!.Emit(OpCodes.Br, startLabel);
                _il!.MarkLabel(endLabel);
            }
            else    // Must be empty statement
            {
                // "nop";
            }
        }
        void LoadIdentityName(string identityName)
        {   // CIL requires that "literals" ( constants ) be propagated
            // This is not an optimization -- it is a requirement
            if (Program.Block.Constants.Exists(i => i.Name == identityName))
            {
                Identity constant = Program.Block.Constants.Single(i => i.Name == identityName);
                LoadConstant(constant.Value);
            }
            else
            {
                var localTable = _methodLocalsTable[_proc!.Name];
                if (localTable.ContainsKey(identityName))
                {
                    _il!.Emit(OpCodes.Ldloc, localTable[identityName]);
                }
                else
                {
                    _il!.Emit(OpCodes.Ldsfld, _globalTable[identityName]);
                }
            }
        }

        void StoreIdentityName(string identityName)
        {
            var localTable = _methodLocalsTable[_proc!.Name];
            if (localTable.ContainsKey(identityName))
            {
                _il!.Emit(OpCodes.Stloc, localTable[identityName]);
            }
            else
            {
                _il!.Emit(OpCodes.Stsfld, _globalTable[identityName]);
            }
        }
        void BranchWhenTrue(Condition condition, Label label)
        {
            if (condition.Type == ConditionType.True)
            {
                _il!.Emit(OpCodes.Br, label);
            }
            else if (condition.Type == ConditionType.False)
            {
                //yield return "nop";
            }
            else if (condition.Type == ConditionType.Odd)
            {
                var oc = (OddCondition) condition;
                GenerateExpression(oc.Expression);
                _il!.Emit(OpCodes.Ldc_I4_1);
                _il!.Emit(OpCodes.And);
                _il!.Emit(OpCodes.Brtrue, label);
            }
            else  // BinaryCondition
            {
                var bc = (BinaryCondition) condition;
                GenerateExpression(bc.FirstExpression);
                GenerateExpression(bc.SecondExpression);
                switch (bc.Type)
                {
                    case ConditionType.Equal:
                        _il!.Emit(OpCodes.Beq, label);
                        break;
                    case ConditionType.NotEqual:
                        _il!.Emit(OpCodes.Bne_Un, label);
                        break;
                    case ConditionType.GreaterThan:
                        _il!.Emit(OpCodes.Bgt, label);
                        break;
                    case ConditionType.LessThan:
                        _il!.Emit(OpCodes.Blt, label);
                        break;
                    case ConditionType.GreaterThanOrEqual:
                        _il!.Emit(OpCodes.Bge, label);
                        break;
                    case ConditionType.LessThanOrEqual:
                        _il!.Emit(OpCodes.Ble, label);
                        break;
                    default:
                        throw new Exception("Unhandled BinaryCondition");
                }
            }
        }

        void BranchWhenFalse(Condition condition, Label label)
        {
            if (condition.Type == ConditionType.True)
            {
                //yield return "nop";
            }
            else if (condition.Type == ConditionType.False)
            {
                _il!.Emit(OpCodes.Br, label);
            }
            else if (condition.Type == ConditionType.Odd)
            {
                var oc = (OddCondition) condition;
                GenerateExpression(oc.Expression);
                _il!.Emit(OpCodes.Ldc_I4_1);
                _il!.Emit(OpCodes.And);
                _il!.Emit(OpCodes.Brfalse, label);
            }
            else // BinaryCondition
            {
                var bc = (BinaryCondition) condition;
                GenerateExpression(bc.FirstExpression);
                GenerateExpression(bc.SecondExpression);

                switch (bc.Type)
                {
                    case ConditionType.Equal:
                        _il!.Emit(OpCodes.Bne_Un, label);
                        break;
                    case ConditionType.NotEqual:
                        _il!.Emit(OpCodes.Beq, label);
                        break;
                    case ConditionType.GreaterThan:
                        _il!.Emit(OpCodes.Ble, label);
                        break;
                    case ConditionType.LessThan:
                        _il!.Emit(OpCodes.Bge, label);
                        break;
                    case ConditionType.GreaterThanOrEqual:
                        _il!.Emit(OpCodes.Blt, label);
                        break;
                    case ConditionType.LessThanOrEqual:
                        _il!.Emit(OpCodes.Bgt, label);
                        break;
                    default:
                        throw new Exception("Unhandled BinaryCondition");
                }
            }
        }
        void GenerateExpression(Expression expression)
        {
            if (expression is RandExpression)
            {
                GenerateRandExpression((RandExpression) expression);
            }
            else
            {
                if (expression.ExpressionNodes.Count == 0)
                {
                    Console.WriteLine("Trying to generate an empty expression ( no nodes )");
                }

                var enumerator = expression.ExpressionNodes.GetEnumerator();
                if (enumerator.MoveNext())
                {
                    GenerateFirstExpressionNode(enumerator.Current);
                }

                while (enumerator.MoveNext())
                {
                    GenerateExpressionNode(enumerator.Current);
                }
            }
        }

        void GenerateRandExpression(RandExpression r)
        {
            // This took me forever - I was calling typeof(Random).GetConstructor() and that does not work
            _il!.Emit(OpCodes.Newobj, typeof(Random).GetConstructors()[0]);
            GenerateExpression(r.LowExpression);
            GenerateExpression(r.HighExpression);
            _il!.Emit(OpCodes.Callvirt, typeof(Random).GetMethod("Next", new[] {typeof(int), typeof(int)}));
        }

        void GenerateFirstExpressionNode(ExpressionNode node)
        {
            if (node.Term.IsSingleConstantFactor)
            {
                ConstantFactor factor = (ConstantFactor) node.Term.FirstFactor;
                int c = Int32.Parse(factor.Value);
                if (c == 1 && !node.IsPositive)
                {
                    _il!.Emit(OpCodes.Ldc_I4_M1);
                }
                else
                {
                    LoadConstant(factor.Value);
                    if (!node.IsPositive)
                    {
                        _il!.Emit(OpCodes.Neg);
                    }
                }
            }
            else
            {
                GenerateTerm(node.Term);
                if (!node.IsPositive)
                {
                    _il!.Emit(OpCodes.Neg);
                } 
            }
        }
        void GenerateExpressionNode(ExpressionNode node)
        {
            GenerateTerm(node.Term);
            _il!.Emit(node.IsPositive ? OpCodes.Add : OpCodes.Sub );
        }
        
        void GenerateTerm(Term term)
        {
            var te = term.TermNodes.GetEnumerator();
            te.MoveNext();
            GenerateFactor(te.Current.Factor);
            while (te.MoveNext())
            {
                GenerateFactor(te.Current.Factor);
                _il!.Emit((te.Current.IsDivision) ? OpCodes.Div : OpCodes.Mul);
            }
        }

        void LoadConstant(string constant)
        {
            int c = Int32.Parse(constant);
            switch (c)
            {
                case 0: _il!.Emit(OpCodes.Ldc_I4_0); break;
                case 1: _il!.Emit(OpCodes.Ldc_I4_1); break;
                case 2: _il!.Emit(OpCodes.Ldc_I4_2); break;
                case 3: _il!.Emit(OpCodes.Ldc_I4_3); break;
                case 4: _il!.Emit(OpCodes.Ldc_I4_4); break;
                case 5: _il!.Emit(OpCodes.Ldc_I4_5); break;
                case 6: _il!.Emit(OpCodes.Ldc_I4_6); break;
                case 7: _il!.Emit(OpCodes.Ldc_I4_7); break;
                case 8: _il!.Emit(OpCodes.Ldc_I4_8); break;
            default:
                if (c >= 0 && c < 128) // Fits in a single byte
                {
                    _il!.Emit(OpCodes.Ldc_I4_S, c);
                }
                else
                {
                    _il!.Emit(OpCodes.Ldc_I4, c);
                }
                break;
            }
        }
        void GenerateFactor(Factor factor)
        {
            if (factor is ConstantFactor)
            {
                ConstantFactor nf = (ConstantFactor) factor;
                LoadConstant(nf.Value);
            }
            else if (factor is IdentityFactor)
            {
                IdentityFactor nf = (IdentityFactor) factor;
                LoadIdentityName(nf.IdentityName);
            }
            else if (factor is ExpressionFactor)
            {
                ExpressionFactor ef = (ExpressionFactor) factor;
                GenerateExpression(ef.Expression);
            }
        }
    }
}