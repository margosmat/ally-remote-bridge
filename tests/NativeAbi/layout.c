// ABI checks against the pinned upstream C/C++ headers. No third-party implementation copied.
#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
typedef struct {
    int8_t lx, ly, rx, ry; uint16_t buttons; uint8_t dpad, l2, r2;
    uint16_t t1x, t1y; uint8_t t1active; uint16_t t2x, t2y; uint8_t t2active;
    int16_t gx, gy, gz, ax, ay, az;
} ds4;
typedef struct { int32_t port; uint32_t hash; char bus[32], service[32], host[1025]; } location;
typedef struct { uint32_t size; location loc; char serial[16]; bool events; } attach;
typedef struct { uint32_t id; int32_t speed; uint16_t vendor, product; char serial[16]; uint8_t serial_index; bool events; } hardware;
typedef struct { location loc; hardware hardware; } imported;
typedef struct { uint32_t size; imported devices[1]; } query;
_Static_assert(sizeof(ds4) == 34, "DS4 ABI size");
_Static_assert(offsetof(ds4, t1x) == 10, "DS4 first touch offset");
_Static_assert(offsetof(ds4, gx) == 22, "DS4 gyro offset");
_Static_assert(sizeof(attach) == 1124, "USB/IP attach ABI size");
_Static_assert(offsetof(attach, loc.host) == 76, "USB/IP host offset");
_Static_assert(offsetof(attach, serial) == 1104, "USB/IP serial offset");
_Static_assert(sizeof(imported) == 1132, "USB/IP imported ABI size");
_Static_assert(offsetof(imported, hardware.serial) == 1112, "USB/IP imported serial offset");
_Static_assert(sizeof(query) == 1136, "USB/IP query ABI size");
int main(void) { return 0; }
