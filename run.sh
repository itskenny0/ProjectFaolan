#!/bin/bash

### user login ingame is "faolan", I think no password required (found in sqlite db)

docker run \
        -p 0.0.0.0:7010:7010 \
        -p 0.0.0.0:7011:7011 \
        -p 0.0.0.0:7012:7012 \
        -p 0.0.0.0:7013:7013 \
        -p 0.0.0.0:7014:7014 \
        faolan
