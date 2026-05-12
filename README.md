# plc
Optimizing Multi-target Extended PL/0 Compiler for .NET written in C#.

The compiler was written using the EBNF found on the PL/0 page of Wikipedia as of September 4, 2021. It is able to compile the three test programs found on that page. They can be found as `wiki.p`, `wiki2.p`, and `wiki3.p` in the `samples` directory. 

This is Hello World in PLC

    ! "Hello World!".
## Mutli-target
The compiler takes PL/0 source code as input and generates C, C#, Basic, PL/0, Python, QBE, RISC-V RV32IM, CIL, or .NET assemblies as output.

### Example:
The following PL/0 program ( first example on Wikipedia )

    VAR x, squ;

    PROCEDURE square;
    BEGIN
        squ:= x * x
    END;

    BEGIN
        x := 1;
        WHILE x <= 10 DO
        BEGIN
            CALL square;
            ! squ;
            x := x + 1
        END
    END.
compiles to the following BASIC code ( with optimization on )

    10 x = 1
    20 PRINT x*x
    30 x = x+1
    40 IF x <= 10 GOTO 20

or to the following C# code

    using System;
    
    public class Program {
        static int x;

        public static void Main() {
            x = 1;
            do {
                Console.WriteLine(x*x);
                x = x+1;
            } while (x<=10);
        }
    }
or to these CIL instructions

    .field private static int32 x

    .method public hidebysig static void Main() cil managed
    {
        .entrypoint
        .maxstack 8
        ldc.i4.1
        stsfld int32 plc::x
    startloop1:
        ldsfld int32 plc::x
        ldsfld int32 plc::x
        mul
        call void [mscorlib]System.Console::WriteLine(int32)
        ldsfld int32 plc::x
        ldc.i4.1
        add
        stsfld int32 plc::x
        ldsfld int32 plc::x
        ldc.i4.s 10
        ble startloop1
        ret
    }

All of the above produce the same output and are semantically equivalent. You can see though that the structure of the generated versions are a bit different from the original PL/0. The section on PLC optimizations will explain why.

Keen eyed optimizers may notice that the expression `x = x+1` was not converted to `x++` in the C# version. Increment does not exist at the CIL level and, even in C#, `x++`, `x += 1`, and `x = x + 1` all compile to identical CIL.

Compiling C# snippet above with the Microsoft compiler produces the same CIL instructions shown here ( `csc.exe` / .NET 6 RC1 ). That said, the C# compiler ( even when told to optimize ) produces substantially less optimized code than PLC when fed code that PLC has not optimized first.
## Extensions
A few additions on top of the base PL/0 syntax
### WRITE statement
Apparently the original version of PL/0 had no input or output instructions and so technically `WRITE` is an extension. That said, it was required to compile the example programs on the Wikipedia page. So too was the alias `!` which is treated as the same statement. Both of them take an expression whose evaluated results are written to the console. In the Wikipedia EBNF, the expression has to evaluate to an integer as PL/0 has only integer types. The `WRITE` in PLC has been extended to optionally take a string as input instead of an integer. This makes the following a valid PLC program ( it can be found as `hello.p` in the `samples` directory ).

    ! "Hello World!"
### READ statement
As with `WRITE`, I needed to add `READ` to compile the Wikipedia examples as well as to add `?` as an alias. As with `WRITE` I added the ability to provide a string. The syntax of `READ` is `READ "string" identifier` where `"string"` is optional and `identifier` must be an integer. Non-integer entries are taken as zero.
### DO WHILE statement
This adds `DO statement WHILE condition`. Unlike a standard `WHILE` loop, the `DO` loop executes its statement at least once regardless of the condition. It was necessary to add this statement in order to implement loop inversion ( see the section on optimizations ). The `WHILE` segment is optional; skipping it results in an infinite loop.
### "FOR" statement
Inspired by the FOR command found in BASIC, PLC features a FOR loop of its own. Instead of adding a new keyword though, it is simply an alternative syntax for `WHILE`. The syntax is `WHILE identifier := exp1 TO exp2 STEP exp3` where `STEP` is optional. Note that the STEP can be an expression and not only a simple integer as it is in most BASIC versions.

Example:

    VAR x;
    WHILE x := 1 TO 25 DO ! x;
is syntactic sugar for

    VAR x;
    
    BEGIN
        x := 1;
        WHILE x <= 25 DO
        BEGIN
            WRITE x;
            x := x+1
        END
    END.
### Curly braces
`{` and `}` are aliases for `BEGIN` and `END` respectively. As they are true aliases, the two styles can be used completely interchangeably.
### RAND expression
`RAND exp1 exp2` takes two expressions and returns a random number in the range from one to the other ( inclusive ). Along with the extensions to `READ` and `WRITE` above, `RAND` makes programs like the following possible ( showing the use of curly braces as well ).
 
    var guess, num;
    
    begin
        num := rand 1 10;
        while guess # num do {
            ? "Guess a number: " guess;
            if guess > num then ! "Lower";
            if guess < num then ! "Higher";
            if guess = num then ! "You nailed it!";
        }
    end.
Here is the C code generated by the above

    #include <stdio.h>
    #include <stdlib.h>
    #include <time.h>
    
    int guess, num;
    
    int main() {
        /* Intializes random number generator */
        srand((unsigned) time(0));
    
        num = (rand() % 10) + 1;
        if (guess!=num) {
            do {
                fputs("Guess a number: ", stdout);
                scanf("%d", &guess);
                if (guess>num) {
                    puts("Lower");
                }
                if (guess<num) {
                    puts("Higher");
                }
                if (guess==num) {
                    puts("You nailed it!");
                }
            } while (guess!=num);
        }
        return 0; /* Success! */
    }


## Optimizations
The motivation for writing PLC was to investigate and understand some of the kinds of optimizations that compilers make.

On .NET, the bytecode generated by PLC will be compiled again by the JIT ( Just-in-Time Compiler ). In .NET, much of the optimization is done in the JIT and so some of the techniques applied in PLC may be redundant.

Writing PLC was just for fun and mostly a learning exercise. PLC itself may not be good for much but these techniques WILL be useful if I ever write my own direct to native code compiler or even my own .NET CLR. Never say never...

### Constant Folding

The following PL/0 program

    var X;
    BEGIN
        ? X;
        ! 3 - 9 + 6*X/2 + 12/3 + 7
    END.
generates the following ( when targeting BASIC with the optimizer on )

    10 INPUT X
    20 PRINT X*3+5
### Constant Propagation
The following PL/0 program

    CONST x = 5;
    VAR squ;
    
    BEGIN
    squ := x * x;
    ! squ
    END.
reduces to the following CIL

    .method public hidebysig static void Main() cil managed
    {
        .entrypoint
        .maxstack 8
        ldc.i4.s 25
        call void [mscorlib]System.Console::WriteLine(int32)
        ret
    }
In the above, PLC `squ` relies only on the constant `x` and therefore is really a constant itself. `squ` being a constant, all references to `squ` are replaced by `25`.
### Procedure Inlining

    VAR x, squ;
    
    PROCEDURE SQUARE;
    BEGIN
        squ := x * x
    END;
    
    BEGIN
        READ x;
        CALL SQUARE;
        WRITE squ
    END.

becomes

    VAR x, squ;
    BEGIN
        READ x;
        squ := x*x;
        WRITE squ
    END.
### Reachability Analysis

    CONST x = 5;
    BEGIN
        IF 6 > x THEN ! "Hello World!"
    END.
Since 6 > 5 is known to always be true, the IF statement can be replaced by the WRITE statement.

    WRITE "Hello World!".
If the condition were reversed ( to `<` ), then `! "Hello World!"` is unreachable which makes it dead code ( see below ).

### Assignment propagation
Variables that are assigned constant values can be moved forward as constants into some instruction types. Specifically for `WRITE` statements, it may be possible to inline entire expressions.

    VAR x, y, squ;
    
    BEGIN
        x := 1;
        IF x <= 10 THEN 
        DO
        BEGIN
            squ := x*x;
            WRITE squ;
            READ y;
            squ := y*y;
            IF y < 6 THEN WRITE squ;
            READ y;
            WRITE y;
            IF y > 2 THEN WRITE squ;
            x := x+1
        END
        WHILE x <= 10
    END.

If the first `READ y` is replaced by the assignment `y := 2`, this results

    VAR x, y, squ;
    
    BEGIN
        x := 1;
        DO
        BEGIN
            squ := x*x;
            WRITE squ;
            y := 2;
            squ := 4;
            WRITE 4;
            READ y;
            WRITE y;
            IF y > 2 THEN WRITE 4;
            x := x+1
        END
        WHILE x <= 10
    END.

The value of `y` is now known through much of the program and it can be replaced by a constant. Values derived from `y`, like `squ` also become known. Reading a new value into 'y' stops it from propagating but 'squ' stays constant a while longer. `x` cannot be propagated as its value changes in the loop. That said, the `IF` statement before the loop has been eliminated.

Replacing the remaining `READ y` results in the following version

    VAR x, squ;
    
    BEGIN
        x := 1;
        DO
        BEGIN
            squ := x*x;
            WRITE squ;
            squ := 4;
            WRITE 4;
            WRITE 3;
            WRITE 4;
            x := x+1
        END
        WHILE x <= 10
    END.
At this point, much more of the program has been converted to constants. Another `IF` statement has been eliminated as has the variable `y` itself. The variable `squ` cannot be removed it is tied to another variable ( `x` ) in the loop.

### Dead Code Elimination
The following PL/0 input

    CONST X = 5;
    VAR Y, SQU;
    
    PROCEDURE SQUARE;
    BEGIN
        SQU := X * X
    END;
    
    BEGIN
        IF 6 > X THEN
        BEGIN
            CALL SQUARE;
            WRITE SQU;
        END
    END.

reduces to identical CIL as the example in Constant Propagation. In both cases, the entire program is reduced to a single `WRITE 25` statement.

Dead code is code that does not contribute to or alter the output or behaviour of the program. For example, the inlined procedure definition is removed. One constants are propagated, the variable Y and constant X also become redundant on unused. Unreachable code is also removed where necessary.

### Loop Inversion
Loops, especially `WHILE` loops like the following are very common.

    VAR x;

    BEGIN
        x := 1;
        WHILE x <= 10 DO
        BEGIN
            WRITE x;
            x := x+1
        END
    END.
In PLC, "FOR" loops produce identical output to the `WHILE` loop above.

To see how the computer executes a `WHILE` loop, it is helpful to look at the generated machine code, the CIL, or the equivalent in BASIC.

With optimizations off, the loop above translates to the following BASIC program.

    10 x = 1
    20 IF x > 10 GOTO 60
    30 PRINT x
    40 x = x+1
    50 GOTO 20
    60 REM END
Key features are the test at the top ( the `IF` statement ) which skips past the loop at the end ( when the condition is no longer satisfied ) and the branch at the bottom ( the `GOTO` ) that starts the loop over again each time ( where the condition gets checked each time ). The condition is always checked at least once ( even if it fails the first time and the loop is never entered ). If the loop is entered, there is at least one `GOTO` for ever trip through the loop. On the final loop there are actually two jumps ( `GOTO` ), first to return to the `IF` statement and then to jump past the loop when the condition is not met.

Loop inversion converts the `WHILE` loop into a `DO / WHILE` wrapped in an `IF` statement.

    VAR x;
    
    BEGIN
        x := 1;
        IF x <= 10 THEN
        DO
        BEGIN
            WRITE x;
            x := x+1
        END
        WHILE x <= 25
    END.
This version produces the same output as the non-inverted loop we started with. The latter version is a bit more verbose and may feel less efficient but it is not.

Here is the inverted loop as a BASIC program

    10 x = 1
    20 IF x > 10 GOTO 60
    30 PRINT x
    40 x = x+1
    50 IF x <= 10 GOTO 30
    60 REM END

If we compare the versions in BASIC ( or CIL or Assembly Language ), we can see that the versions are not as different as they look in PL/0, C, or C#. 

In fact, the only differce is the the `GOTO` statement at the end has now become an `IF` statement. It is not an extra statement though. We can see that the loop now jumps back to just after the first `IF` statement. There is still only one `IF` statement per loop. Instead of jumping to the end of the loop when the test fails, the `IF / GOTO` statement jumps to the beginning of the loop when the test succeeds. On the very last trip through the loop, the two `GOTO` jumps described above are not necessary. It is not necessary to jump to the start of the loop to test the condition and it is not neccessary to jump to the end of the loop when the condition is not met ( as we are already at the end of the loop ). Thus, the inverted loop is actully two `GOTO` jumps shorter than the non--inverted one.

As an added bonus, the first `IF` statement now only applies to the first time through the loop. In the code above, `x` is set to `1` just before the `IF` statement and we know that `1 > 10` is false. So, the first `IF` statement can be safely removed.

The inverted loop version is one `IF` test and two `GOTO` jumps shorter than the non-inverted one.

Here is the fully optimized version in BASIC

    10 x = 1
    20 PRINT x
    30 x = x+1
    40 IF x <= 10 GOTO 20
### Tail Call Elimination
In some instances, PLC is able to eliminate a recursive procedure call by replacing it with a loop. The procedure may then be inlined and removed.

Before:

    VAR n, f;
    
    PROCEDURE fact;
    BEGIN
        IF n > 1 THEN
        BEGIN
            f := n * f;
            n := n - 1;
            CALL fact
        END
    END;
    
    BEGIN
        ? "Enter a number " n;
        f := 1;
        CALL fact;
        !f
    END.
After:

    VAR n, f;
    
    BEGIN
        READ "Enter a number " n;
        f := 1;
        IF n > 1 THEN
        DO
        BEGIN
            f := n*f;
            n := n-1
        END
        WHILE n > 1;
        WRITE f
    END.
The recursive call to `fact` has been replaced by a loop. The loop itself has been inverted as per the discussion above.
