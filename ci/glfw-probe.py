# Diagnostic only: initialises the bundled GLFW library directly and prints its error message.
import ctypes, glob, sys
path = glob.glob('src/Talt.Client/bin/Release/net10.0/runtimes/*/native/libglfw.so.3')[0] if len(sys.argv) < 2 else sys.argv[1]
print('library', path)
glfw = ctypes.CDLL(path)
callback_type = ctypes.CFUNCTYPE(None, ctypes.c_int, ctypes.c_char_p)
def on_error(code, message):
    print('GLFW error', hex(code), message.decode())
keep = callback_type(on_error)
glfw.glfwSetErrorCallback(keep)
print('glfwInit ->', glfw.glfwInit())
