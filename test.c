#include <stdio.h>

int x;

int main() {
    x = 1;
    do {
        printf("%d\n", x*x);
        x = x+1;
    } while (x<=10);
    return 0; /* Success! */
}
