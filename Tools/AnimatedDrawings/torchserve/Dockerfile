# syntax = docker/dockerfile:1.2

FROM continuumio/miniconda3:24.1.2-0

# install os dependencies
RUN mkdir -p /usr/share/man/man1
RUN printf '%s\n' \
    'deb https://deb.debian.org/debian bullseye main' \
    'deb https://deb.debian.org/debian bullseye-updates main' \
    > /etc/apt/sources.list && \
    printf '%s\n' \
    'Acquire::Retries "8";' \
    'Acquire::ForceIPv4 "true";' \
    'Acquire::http::Pipeline-Depth "0";' \
    'Acquire::https::Timeout "120";' \
    > /etc/apt/apt.conf.d/80retries && \
    apt-get update && \
    DEBIAN_FRONTEND=noninteractive apt-get install --no-install-recommends -y \
    ca-certificates \
    openjdk-11-jre-headless \
    git \
    gcc \
    g++ \
    make \
    && rm -rf /var/lib/apt/lists/*

RUN conda install python=3.8.13 -y


# install python dependencies
ENV PIP_NO_CACHE_DIR=1
RUN pip install --no-cache-dir openmim
RUN pip install --no-cache-dir torch==1.13.1+cpu torchvision==0.14.1+cpu --index-url https://download.pytorch.org/whl/cpu
RUN pip install --no-cache-dir --no-deps https://download.openmmlab.com/mmcv/dist/cpu/torch1.13.0/mmcv_full-1.7.0-cp38-cp38-manylinux1_x86_64.whl
RUN pip install --no-cache-dir --no-deps mmdet==2.27.0
RUN pip install --no-cache-dir torchserve==0.6.1

# bugfix for xtcocoapi, an mmpose dependency
RUN git clone https://github.com/jin-s13/xtcocoapi.git
WORKDIR xtcocoapi
RUN pip install --no-cache-dir -r requirements.txt
RUN python setup.py install
WORKDIR /
RUN pip install --no-cache-dir addict==2.4.0 chumpy==0.70 json-tricks==3.17.3 munkres==1.1.4 yapf==0.40.2 opencv-python-headless==4.9.0.80 terminaltables==3.1.10 pycocotools==2.0.7
RUN pip install --no-cache-dir --no-deps mmpose==0.29.0
RUN pip install --no-cache-dir numpy==1.24.4  #solve numpy version problem

# prep torchserve
COPY model-store/ /home/torchserve/model-store/
COPY config.properties /home/torchserve/config.properties

# starting command
CMD /opt/conda/bin/torchserve --start --ts-config /home/torchserve/config.properties && sleep infinity
