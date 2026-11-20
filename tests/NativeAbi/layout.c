// ABI checks against the pinned upstream C/C++ headers. No third-party implementation copied.
#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
typedef struct {
    int8_t lx, ly, rx, ry; uint16_t buttons; uint8_t dpad, l2, r2;
    uint16_t t1x, t1y; uint8_t t1active; uint16_t t2x, t2y; uint8_t t2active;
    int16_t gx, gy, gz, ax, ay, az;
} ds4;
typedef struct { char bus[32], service[32], host[1025]; } location;
typedef struct { location loc; char serial[16]; bool events; } config;
typedef struct { uint32_t size; int32_t port; uint32_t hash; config config; } attach;
typedef struct { uint32_t id; int32_t speed; uint16_t vendor, product; uint8_t serial_index; } hardware;
typedef struct { int32_t port; uint32_t hash; config config; hardware hardware; } imported;
_Static_assert(sizeof(ds4) == 34, "DS4 ABI size");
_Static_assert(offsetof(ds4, t1x) == 10, "DS4 first touch offset");
_Static_assert(offsetof(ds4, gx) == 22, "DS4 gyro offset");
_Static_assert(sizeof(attach) == 1120, "USB/IP attach ABI size");
_Static_assert(offsetof(attach, config.loc.host) == 76, "USB/IP host offset");
_Static_assert(offsetof(attach, config.serial) == 1101, "USB/IP serial offset");
_Static_assert(sizeof(imported) == 1132, "USB/IP imported ABI size");
_Static_assert(offsetof(imported, config.serial) == 1097, "USB/IP imported serial offset");
int main(void) { return 0; }
