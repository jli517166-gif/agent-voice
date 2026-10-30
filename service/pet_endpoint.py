"""Keep the unauthenticated dsh-pet bridge confined to loopback."""
from urllib.parse import urlsplit


def validate_endpoint(value):
    if not isinstance(value,str) or len(value)>200:raise ValueError('桌宠 Web 地址无效')
    parsed=urlsplit(value.rstrip('/'))
    try:port=parsed.port
    except ValueError as error:raise ValueError('桌宠 Web 端口无效') from error
    if parsed.scheme!='http' or parsed.hostname!='127.0.0.1' or not port or parsed.path!='/dsh-pet-7340' or parsed.query or parsed.fragment or parsed.username or parsed.password:
        raise ValueError('桌宠 Web 地址须为 http://127.0.0.1:<端口>/dsh-pet-7340')
    return f'http://127.0.0.1:{port}/dsh-pet-7340'
