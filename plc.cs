using System;
using System.IO;
using System.Collections.Generic;

namespace PLC
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: plc inputfile outputfile");
                return;
            }
            string inFilename = args[0];
 
            string outFilename;
            if (args.Length > 1)
            {
                outFilename = args[1];
            }
            else
            {
                outFilename = Path.GetFileNameWithoutExtension(inFilename);
            }
            string extension = Path.GetExtension(outFilename);
            if (string.IsNullOrEmpty(extension))
            {
                // For .NET executables, use .exe extension
                outFilename = outFilename + ".exe";
                extension = ".exe";
            }
            using (var fileStream = new FileStream(inFilename, FileMode.Open)) {
                ParsedProgram program = new Parser().Parse(new Scanner().Scan(fileStream));
                program = new Optimizer().Optimize(program);
                IGenerator generator;
                switch (extension)
                {
                    case ".p":
                        generator = new PL0Generator(program);
                        break;
                    case ".bas":
                        generator = new BasicGenerator(program);
                        break;
                    case ".il":
                    case ".cil":
                    case ".msil":
                        generator = new CIL_Generator(program);
                        break;
                    case ".c":
                        generator = new CGenerator(program);
                        break;
                    case ".cs":
                        generator = new CSharpGenerator(program);
                        break;
		            case ".qbe":
			            generator = new QBEGenerator(program);
			            break;
		            case ".s":
			            generator = new RV32Generator(program);
			            break;
		            case ".py":
			            generator = new PythonGenerator(program);
			            break;
		            case ".exe":
			            generator = new CLRGenerator(program);
			            break;
                    default:
                        throw new Exception("Unknown extension: " + extension);
                }
                // Output the generated text to the screen unless we are generating an executable
		        if (!(generator is CLRGenerator)) {
                    foreach (string s in generator.Generate()) Console.WriteLine(s);
		        }
                generator.Compile(outFilename);
            }
        }
        
        static IEnumerable<string> LinesFromFile(string filename) {
            var fileStream = new FileStream(filename, FileMode.Open);
            var reader = new StreamReader(fileStream);
            string line;
            while ((line = reader.ReadLine()) != null) {
                yield return line;
            }
        }

        static IEnumerable<char> CharactersFromFile(string filename) {
            var fileStream = new FileStream(filename, FileMode.Open);
            var reader = new StreamReader(fileStream);
            int c;
            while ((c = reader.Read()) != -1) {
                yield return (char) c;
            }
        }
    }
}
